using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PriceRadar.Core.Domain;
using PriceRadar.Core.Matching;
using PriceRadar.Core.Normalization;
using PriceRadar.Core.Pricing;
using PriceRadar.Infrastructure.Ai;
using PriceRadar.Infrastructure.Matching;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Queries;

public enum LookupMatchStatus
{
    Matched,
    Ambiguous,
    NoMatch,
}

public sealed record LookupCandidate(int BookId, string Title, string? Author, decimal Score, int ActiveListings);

public sealed record LookupMatch(LookupMatchStatus Status, string? Method, int? BookId, string? Title, string? Author, decimal? Score,
    IReadOnlyList<LookupCandidate> Candidates);

public sealed record LookupQueryEcho(string Title, string? Author, decimal AskingPrice, string NormTitle, string NormAuthor);

public sealed record LookupResult(LookupQueryEcho Query, LookupMatch Match, Verdict? Verdict, Confidence? Confidence, BookStats? Stats,
    string Explanation, long ElapsedMs);

/// <summary>
/// GET /api/lookup (FR-15, spec §8.4): normalise the query, find the canonical book with the same matcher as the
/// pipeline, compare the asking price with its statistics. Never calls the Claude API (D-19): an ambiguous query is
/// answered from match.MatchCache when the pipeline already resolved the same string, otherwise the candidates are
/// returned for the user to pick.
/// </summary>
public sealed class LookupService(
    PriceRadarDbContext db,
    BookStatsQuery stats,
    BookCatalogProvider catalogs,
    MatchCacheService cache,
    IOptions<MatchingSettings> matching,
    IOptions<AiSettings> ai)
{
    private const int MaxCandidates = 3;

    public async Task<LookupResult> LookupAsync(string title, string? author, decimal askingPrice, int windowDays, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var normTitle = TextNormalizer.NormalizeTitle(title);
        var normAuthor = TextNormalizer.NormalizeAuthor(author);
        var echo = new LookupQueryEcho(title, author, askingPrice, normTitle, normAuthor);

        var match = await MatchAsync(normTitle, normAuthor, ct);
        if (match.Status != LookupMatchStatus.Matched)
        {
            var explanation = match.Status == LookupMatchStatus.Ambiguous
                ? match.Candidates.Count == 1
                    ? "One book could match; confirm it to see its price statistics."
                    : $"{match.Candidates.Count} books could match; pick one to see its price statistics."
                : "No book in the catalogue matches this title and author.";
            return new LookupResult(echo, match, match.Status == LookupMatchStatus.NoMatch ? Verdict.Unknown : null, null, null,
                explanation, sw.ElapsedMilliseconds);
        }

        var bookStats = await stats.GetStatsAsync(match.BookId!.Value, windowDays, ct: ct);
        var verdict = VerdictPolicy.Decide(askingPrice,
            new PriceDistribution(bookStats.SampleSize, bookStats.P25, bookStats.Median, bookStats.P75, windowDays));
        return new LookupResult(echo, match, verdict.Verdict, verdict.Confidence, bookStats, verdict.Explanation, sw.ElapsedMilliseconds);
    }

    private async Task<LookupMatch> MatchAsync(string normTitle, string normAuthor, CancellationToken ct)
    {
        if (normTitle.Length == 0)
        {
            return NoMatch();
        }

        var catalog = await catalogs.GetAsync(ct);
        var decision = new MatchDecider(catalog.Index, matching.Value.ToOptions()).Decide(normTitle, normAuthor);

        switch (decision.Outcome)
        {
            case MatchOutcome.AutoMatch:
                var b = decision.Best!;
                return new LookupMatch(LookupMatchStatus.Matched, b.Score >= 1.0 ? "rule" : "fuzzy", b.Book.BookId, b.Book.Title, b.Book.Author,
                    Round(b.Score), []);

            case MatchOutcome.Ambiguous:
                var cached = await cache.FindAsync(MatchCacheService.Hash(ai.Value.PromptVersion, normTitle, normAuthor), ct);
                if (cached is { Decision: MatchDecision.Match, ResolvedBookId: { } id } && catalog.Index.Get(id) is { } book)
                {
                    return new LookupMatch(LookupMatchStatus.Matched, "ai-cache", id, book.Title, book.Author, cached.Confidence, []);
                }

                return new LookupMatch(LookupMatchStatus.Ambiguous, null, null, null, null, Round(decision.Best!.Score),
                    await WithListingCountsAsync(decision.Candidates.Take(MaxCandidates).ToList(), ct));

            default:
                return NoMatch();
        }
    }

    private async Task<IReadOnlyList<LookupCandidate>> WithListingCountsAsync(List<ScoredCandidate> candidates, CancellationToken ct)
    {
        var ids = candidates.Select(c => c.Book.BookId).ToList();
        var active = await db.Listings.AsNoTracking()
            .Where(l => l.BookId != null && ids.Contains(l.BookId.Value) && l.Status == ListingStatus.Active)
            .GroupBy(l => l.BookId!.Value)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count, ct);
        return candidates.Select(c => new LookupCandidate(c.Book.BookId, c.Book.Title, c.Book.Author, Round(c.Score), active.GetValueOrDefault(c.Book.BookId)))
            .ToList();
    }

    private static LookupMatch NoMatch() => new(LookupMatchStatus.NoMatch, null, null, null, null, null, []);

    private static decimal Round(double score) => Math.Round((decimal)score, 3);
}
