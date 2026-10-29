using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Jobs;

/// <summary>
/// Step 3: resolve ambiguous matches with the MatchCache / Claude API (spec §6.5–6.6). DAY-3 stub: MatchJob produces no
/// ambiguous rows yet, so this only advances the stage. The real implementation lands on DAY-7.
/// </summary>
public sealed class ResolveJob(PriceRadarDbContext db, ILogger<ResolveJob> logger)
{
    [Queue(HangfireQueues.Match)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [30, 120])]
    public async Task ExecuteAsync(long scrapeRunId, CancellationToken ct)
    {
        var run = await db.ScrapeRuns.SingleAsync(r => r.ScrapeRunId == scrapeRunId, ct);
        if (run.Stage >= RunStage.Resolved)
        {
            return;
        }

        run.Stage = RunStage.Resolved;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Run {RunId}: no ambiguous matches to resolve (AI resolution arrives on DAY-7)", scrapeRunId);
    }
}
