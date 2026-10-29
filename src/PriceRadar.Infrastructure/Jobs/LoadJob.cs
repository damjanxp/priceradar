using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Jobs;

/// <summary>
/// Step 4: load the warehouse, apply the sold heuristic and finish the run — all in one transaction, so the presence
/// update can never be applied twice for the same run (spec §9.2–9.3).
/// </summary>
public sealed class LoadJob(PriceRadarDbContext db, IOptions<SoldSettings> sold, TimeProvider clock, ILogger<LoadJob> logger)
{
    [Queue(HangfireQueues.Load)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [15, 60, 180])]
    public async Task ExecuteAsync(long scrapeRunId, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = scrapeRunId, ["Job"] = nameof(LoadJob) });

        var strategy = db.Database.CreateExecutionStrategy();
        var loaded = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var run = await db.ScrapeRuns.SingleAsync(r => r.ScrapeRunId == scrapeRunId, ct);
            if (run.Stage >= RunStage.Loaded)
            {
                return false;
            }

            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));
            await db.Database.ExecuteSqlRawAsync("EXEC dw.usp_LoadRun @ScrapeRunId",
                [new SqlParameter("@ScrapeRunId", scrapeRunId)], ct);
            await db.Database.ExecuteSqlRawAsync("EXEC dw.usp_UpdateListingPresence @ScrapeRunId, @Threshold, @WindowDays",
                [new SqlParameter("@ScrapeRunId", scrapeRunId), new SqlParameter("@Threshold", sold.Value.MissedRunsThreshold),
                 new SqlParameter("@WindowDays", sold.Value.WindowDays)], ct);

            await db.Entry(run).ReloadAsync(ct);   // counters were written by the procedure
            run.Stage = RunStage.Loaded;
            run.Status = RunStatus.Succeeded;
            run.FinishedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Run {RunId} succeeded: {Loaded} loaded, {Rejected} rejected in {Seconds:0.0} s",
                scrapeRunId, run.ListingsLoaded, run.ListingsRejected,
                (run.FinishedAtUtc - (run.StartedAtUtc ?? run.CreatedAtUtc))?.TotalSeconds);
            return true;
        });

        if (!loaded)
        {
            logger.LogInformation("Run {RunId} already loaded; nothing to do", scrapeRunId);
        }
    }
}
