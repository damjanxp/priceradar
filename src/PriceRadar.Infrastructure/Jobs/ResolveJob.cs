using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Core.Matching;
using PriceRadar.Core.Normalization;
using PriceRadar.Infrastructure.Ai;
using PriceRadar.Infrastructure.Matching;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Jobs;

/// <summary>
/// Step 3: resolve the Pending (ambiguous) listings of a run (spec §6.5–6.6). Identical normalised strings are grouped
/// so one answer serves all of them; each group is answered from match.MatchCache or, within the per-run and daily
/// budget, by the Claude API. AI is optional: when it is off, over budget or failing, the rows stay Pending, the run
/// still loads, and a later run retries them.
/// </summary>
public sealed class ResolveJob(
    PriceRadarDbContext db,
    IAiMatchResolver resolver,
    MatchCacheService cache,
    IOptions<AiSettings> ai,
    IOptions<MatchingSettings> matching,
    ILogger<ResolveJob> logger)
{
    [Queue(HangfireQueues.Match)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [30, 120])]
    public async Task ExecuteAsync(long scrapeRunId, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = scrapeRunId, ["Job"] = nameof(ResolveJob) });

        var run = await db.ScrapeRuns.SingleAsync(r => r.ScrapeRunId == scrapeRunId, ct);
        if (run.Stage >= RunStage.Resolved)
        {
            return;
        }

        var pending = await db.StagingListings.AsNoTracking()
            .Where(s => s.ScrapeRunId == scrapeRunId && s.ProcessStatus == ProcessStatus.Normalized && s.MatchStatus == MatchStatus.Pending)
            .ToListAsync(ct);

        var stats = new ResolveStats();
        if (pending.Count > 0)
        {
            var results = await ResolveAsync(pending, stats, ct);
            await MatchJob.ApplyAsync(db, scrapeRunId, results, ct);
        }

        run.Stage = RunStage.Resolved;
        run.AiCalls += stats.Calls;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Run {RunId}: {Pending} ambiguous listings in {Groups} groups → {Calls} Claude calls, {Hits} cache hits, {Left} left pending ({Reason}); cost {CostUsd:0.00000} USD",
            scrapeRunId, pending.Count, stats.Groups, stats.Calls, stats.CacheHits, stats.LeftPending, stats.PendingReason ?? "none", stats.CostUsd);
    }

    private async Task<List<MatchResult>> ResolveAsync(List<StagingListing> pending, ResolveStats stats, CancellationToken ct)
    {
        var settings = ai.Value;
        var catalog = await BookCatalog.LoadAsync(db, ct);
        var decider = new MatchDecider(catalog.Index, matching.Value.ToOptions());
        var spentToday = await cache.SpentTodayUsdAsync(settings, ct);
        var aiAvailable = settings.IsActive;
        if (!aiAvailable)
        {
            stats.PendingReason = "AI disabled (no Ai:ApiKey or Ai:Enabled=false)";
        }

        var results = new List<MatchResult>();
        foreach (var group in pending.GroupBy(p => (p.NormTitle!, p.NormAuthor ?? string.Empty)))
        {
            stats.Groups++;
            var (normTitle, normAuthor) = group.Key;
            var sample = group.First();
            var hash = MatchCacheService.Hash(settings.PromptVersion, normTitle, normAuthor);

            // A book may have appeared since MatchJob ran (another run, an earlier group): re-decide first.
            var decision = decider.Decide(normTitle, normAuthor);
            if (decision.Outcome != MatchOutcome.Ambiguous)
            {
                results.AddRange(group.Select(row => ToResult(row, decision.Outcome == MatchOutcome.AutoMatch ? MatchStatus.AutoMatched : MatchStatus.NewBook,
                    decision.BookId, (decimal?)decision.Best?.Score, BookOrigin.Rule, needsReview: false, catalog.AuthorKeys)));
                continue;
            }

            AiMatchAnswer? answer = null;
            if (await cache.FindAsync(hash, ct) is { } cached)
            {
                stats.CacheHits++;
                await cache.RecordHitAsync(cached.MatchCacheId, ct);
                answer = new AiMatchAnswer(cached.Decision, cached.ResolvedBookId, cached.Confidence, cached.Reason, cached.Model,
                    cached.InputTokens, cached.OutputTokens, cached.LatencyMs);
            }
            else if (aiAvailable && stats.Calls >= settings.MaxCallsPerRun)
            {
                stats.PendingReason = $"per-run limit of {settings.MaxCallsPerRun} calls reached";
            }
            else if (aiAvailable && spentToday + stats.CostUsd >= settings.DailyBudgetUsd)
            {
                stats.PendingReason = $"daily budget of {settings.DailyBudgetUsd} USD reached";
            }
            else if (aiAvailable)
            {
                var request = new AiMatchRequest(sample.RawTitle, sample.RawAuthor, normTitle, normAuthor,
                    decision.Candidates.Select(c => new AiCandidate(c.Book.BookId, c.Book.Title, c.Book.Author, c.Score)).ToList());
                try
                {
                    answer = await resolver.ResolveAsync(request, ct);
                    stats.Calls++;
                    stats.CostUsd += settings.CostUsd(answer.InputTokens, answer.OutputTokens);
                    await cache.SaveAsync(hash, settings, request, answer, ct);
                }
                catch (AiResolverException ex)
                {
                    // Bad key or rejected request: every further call would fail the same way.
                    logger.LogError(ex, "Claude rejected the request; AI resolution stops for this run");
                    aiAvailable = false;
                    stats.PendingReason = $"Claude API error {ex.StatusCode}";
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    logger.LogWarning(ex, "Claude call failed after retries for {NormTitle}; the group stays pending", normTitle);
                    stats.PendingReason = "transient Claude API failure";
                }
            }

            if (answer is null || answer.Decision == MatchDecision.Error)
            {
                stats.LeftPending += group.Count();
                if (answer is not null)
                {
                    stats.PendingReason = $"answer rejected: {answer.Reason}";
                }

                continue;   // rows stay Pending; nothing to write
            }

            results.AddRange(group.Select(row => answer.Decision switch
            {
                MatchDecision.Match => ToResult(row, MatchStatus.AiMatched, answer.BookId, answer.Confidence, BookOrigin.Ai, false, catalog.AuthorKeys),
                MatchDecision.NewBook => ToResult(row, MatchStatus.NewBook, null, answer.Confidence, BookOrigin.Ai, false, catalog.AuthorKeys),
                _ => ToResult(row, MatchStatus.NewBook, null, answer.Confidence, BookOrigin.Ai, needsReview: true, catalog.AuthorKeys),   // unsure
            }));
        }

        return results;
    }

    /// <summary>The TVP overwrites every column, so each row carries its full normalised state, not only the decision.</summary>
    private static MatchResult ToResult(StagingListing row, MatchStatus status, int? bookId, decimal? score, BookOrigin origin, bool needsReview,
        IReadOnlySet<string> authorKeys)
    {
        var n = TextNormalizer.Normalize(row.RawTitle, row.RawAuthor, authorKeys);
        return new MatchResult(row.StagingListingId)
        {
            PriceRsd = row.PriceRsd,
            NormTitle = row.NormTitle!,
            NormAuthor = row.NormAuthor ?? string.Empty,
            BookId = bookId,
            MatchStatus = status,
            MatchScore = score is { } s ? Math.Round(s, 4) : null,
            NewBookTitle = n.DisplayTitle,
            NewBookAuthor = n.DisplayAuthor,
            NewBookOrigin = origin,
            NewBookNeedsReview = needsReview,
        };
    }

    private sealed class ResolveStats
    {
        public int Groups { get; set; }
        public int Calls { get; set; }
        public int CacheHits { get; set; }
        public int LeftPending { get; set; }
        public decimal CostUsd { get; set; }
        public string? PendingReason { get; set; }
    }
}
