using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Persistence;
using PriceRadar.Infrastructure.Sources;
using PriceRadar.Infrastructure.Staging;

namespace PriceRadar.Infrastructure.Jobs;

/// <summary>Step 1: stream the source into ingest.StagingListing (spec §9.2).</summary>
public sealed class ScrapeJob(PriceRadarDbContext db, ListingSourceRegistry sources, StagingBulkWriter writer, TimeProvider clock, ILogger<ScrapeJob> logger)
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(15);

    [Queue(HangfireQueues.Ingest)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public async Task ExecuteAsync(long scrapeRunId, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = scrapeRunId, ["Job"] = nameof(ScrapeJob) });

        var run = await db.ScrapeRuns.Include(r => r.Source).SingleAsync(r => r.ScrapeRunId == scrapeRunId, ct);
        if (run.Stage >= RunStage.Scraped)
        {
            logger.LogInformation("Run {RunId} already scraped; nothing to do", scrapeRunId);
            return;
        }

        run.Status = RunStatus.Running;
        run.StartedAtUtc ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Budget);

        var request = new ScrapeRequest
        {
            SourceCode = run.Source!.Code,
            ScopeKey = run.ScopeKey,
            Query = run.Query,
            MaxPages = run.MaxPages,
            FixtureSet = run.FixtureSet,
        };

        // Listings are stamped with the real scrape time, also for fixture replays, so stats windows stay current.
        var result = await writer.WriteAsync(scrapeRunId, sources.Get(request.SourceCode).FetchAsync(request, timeout.Token),
            () => clock.GetUtcNow().UtcDateTime, timeout.Token);

        run.Stage = RunStage.Scraped;
        run.PagesFetched = request.Progress.PagesFetched;
        run.IsComplete = request.Progress.IsComplete;
        run.ListingsFetched = result.Written;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Run {RunId}: staged {Count} listings from {Source} ({Pages} pages, complete={Complete}, duplicates={Duplicates})",
            scrapeRunId, result.Written, request.SourceCode, run.PagesFetched, run.IsComplete, result.DuplicatesSkipped);
    }
}
