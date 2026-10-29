using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceRadar.Core.Domain;
using PriceRadar.Core.Normalization;
using PriceRadar.Infrastructure.Jobs;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;
using PriceRadar.Infrastructure.Sources;

namespace PriceRadar.Infrastructure.Ingestion;

public sealed record CreateRunCommand(string Source, string? Query, short? MaxPages, string? FixtureSet);

public enum CreateRunOutcome
{
    Created,
    ExistingForIdempotencyKey,
    SourceNotFound,
    SourceDisabled,
    AlreadyActive,
    Invalid,
}

public sealed record CreateRunResult(CreateRunOutcome Outcome, ScrapeRun? Run = null, long? ActiveRunId = null, string? Error = null);

/// <summary>Creates ingestion runs and enqueues the Scrape → Match → Resolve → Load chain (FR-2, FR-3, spec §9).</summary>
public sealed class IngestionService(
    PriceRadarDbContext db,
    ListingSourceRegistry sources,
    IBackgroundJobClient jobs,
    IOptions<PricingSettings> pricing,
    TimeProvider clock,
    ILogger<IngestionService> logger)
{
    public async Task<CreateRunResult> CreateRunAsync(CreateRunCommand command, Guid? idempotencyKey, string requestedBy, CancellationToken ct)
    {
        if (idempotencyKey is { } key
            && await db.ScrapeRuns.AsNoTracking().SingleOrDefaultAsync(r => r.IdempotencyKey == key, ct) is { } replay)
        {
            return new CreateRunResult(CreateRunOutcome.ExistingForIdempotencyKey, replay);
        }

        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Code == command.Source, ct);
        if (source is null)
        {
            return new CreateRunResult(CreateRunOutcome.SourceNotFound, Error: $"Unknown source '{command.Source}'.");
        }

        if (!source.IsEnabled || !sources.Contains(source.Code))
        {
            return new CreateRunResult(CreateRunOutcome.SourceDisabled, Error: $"Source '{source.Code}' is disabled or cannot be ingested from the API.");
        }

        if (source.Kind == SourceKind.Fixture && string.IsNullOrWhiteSpace(command.FixtureSet))
        {
            return new CreateRunResult(CreateRunOutcome.Invalid, Error: "fixtureSet is required for fixture sources, e.g. \"run-001\".");
        }

        // A missing file is not transient: reject it now instead of letting ScrapeJob retry for minutes while the
        // source stays blocked by its active run.
        if (sources.Get(source.Code) is FixtureListingSource fixture && !File.Exists(fixture.PathFor(command.FixtureSet!)))
        {
            return new CreateRunResult(CreateRunOutcome.Invalid, Error: $"Fixture set '{command.FixtureSet}' does not exist for source '{source.Code}'.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var run = new ScrapeRun
        {
            SourceId = source.SourceId,
            ScopeKey = ScopeKeyFor(source, command.Query),
            Query = string.IsNullOrWhiteSpace(command.Query) ? null : command.Query.Trim(),
            FixtureSet = source.Kind == SourceKind.Fixture ? command.FixtureSet : null,
            Status = RunStatus.Queued,
            Stage = RunStage.Created,
            MaxPages = command.MaxPages ?? 10,
            EurRsdRate = pricing.Value.EurRsdRate,
            IdempotencyKey = idempotencyKey ?? Guid.NewGuid(),
            RequestedBy = requestedBy,
            CreatedAtUtc = now,
        };
        db.ScrapeRuns.Add(run);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            db.Entry(run).State = EntityState.Detached;
            if (sql.Message.Contains("UX_ScrapeRun_IdempotencyKey", StringComparison.Ordinal) && idempotencyKey is { } k)
            {
                return new CreateRunResult(CreateRunOutcome.ExistingForIdempotencyKey,
                    await db.ScrapeRuns.AsNoTracking().SingleAsync(r => r.IdempotencyKey == k, ct));
            }

            // UX_ScrapeRun_ActivePerSource: the database, not this code, decides that a run is already active (FR-3).
            var active = await db.ScrapeRuns.AsNoTracking()
                .Where(r => r.SourceId == source.SourceId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running))
                .Select(r => (long?)r.ScrapeRunId).FirstOrDefaultAsync(ct);
            return new CreateRunResult(CreateRunOutcome.AlreadyActive, ActiveRunId: active,
                Error: $"Run {active} for source '{source.Code}' is still active.");
        }

        try
        {
            var scrape = jobs.Enqueue<ScrapeJob>(j => j.ExecuteAsync(run.ScrapeRunId, CancellationToken.None));
            var match = jobs.ContinueJobWith<MatchJob>(scrape, j => j.ExecuteAsync(run.ScrapeRunId, CancellationToken.None));
            var resolve = jobs.ContinueJobWith<ResolveJob>(match, j => j.ExecuteAsync(run.ScrapeRunId, CancellationToken.None));
            jobs.ContinueJobWith<LoadJob>(resolve, j => j.ExecuteAsync(run.ScrapeRunId, CancellationToken.None));
            run.HangfireJobId = scrape;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not enqueue jobs for run {RunId}", run.ScrapeRunId);
            run.Status = RunStatus.Failed;
            run.ErrorMessage = $"Enqueue failed: {ex.Message}";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        logger.LogInformation("Run {RunId} queued for {Source} (scope {ScopeKey}, job {JobId})",
            run.ScrapeRunId, source.Code, run.ScopeKey, run.HangfireJobId);
        return new CreateRunResult(CreateRunOutcome.Created, run);
    }

    /// <summary>Fixture replays and category crawls share a category scope; free-text queries get their own.</summary>
    private static string ScopeKeyFor(Source source, string? query) =>
        source.Kind == SourceKind.Fixture || string.IsNullOrWhiteSpace(query)
            ? $"{source.Code}:category:knjige"
            : $"{source.Code}:q:{SerbianText.FoldToAscii(query).Replace(' ', '-')}";
}
