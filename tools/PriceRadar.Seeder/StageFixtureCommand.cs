using Microsoft.EntityFrameworkCore;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Persistence;
using PriceRadar.Infrastructure.Sources;
using PriceRadar.Infrastructure.Staging;

namespace PriceRadar.Seeder;

/// <summary>
/// Dev-only path (DAY-2, before Hangfire exists): replays a fixture set into ingest.StagingListing under a new
/// ScrapeRun. From DAY-3 the same work is done by ScrapeJob behind POST /api/ingestion/runs.
/// </summary>
internal sealed class StageFixtureCommand(PriceRadarDbContext db, ListingSourceRegistry sources, StagingBulkWriter writer)
{
    public async Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        var set = cli.String("set") ?? throw new ArgumentException("--set is required, e.g. --set run-001.");
        var code = cli.String("source") ?? "fixture";
        var source = sources.Get(code);
        var sourceRow = await db.Sources.SingleOrDefaultAsync(s => s.Code == code, ct)
            ?? throw new InvalidOperationException($"Source '{code}' is not in ingest.Source; run the migrations first.");

        var now = DateTime.UtcNow;
        var run = new ScrapeRun
        {
            SourceId = sourceRow.SourceId,
            ScopeKey = $"{code}:category:knjige",
            // Staged only: Succeeded keeps it out of the one-active-run-per-source index; Stage says how far it got.
            Status = RunStatus.Succeeded,
            Stage = RunStage.Created,
            MaxPages = 10,
            EurRsdRate = 117.2m,
            IdempotencyKey = Guid.NewGuid(),
            RequestedBy = "seeder:stage-fixture",
            CreatedAtUtc = now,
            StartedAtUtc = now,
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var request = new ScrapeRequest { SourceCode = code, ScopeKey = run.ScopeKey, FixtureSet = set };
        StagingWriteResult result;
        try
        {
            result = await writer.WriteAsync(run.ScrapeRunId, source.FetchAsync(request, ct),
                () => request.Progress.CapturedAtUtc ?? DateTime.UtcNow, ct);
        }
        catch (Exception ex)
        {
            // Same rule as ScrapeJob (spec §9.3): a run that cannot finish is marked Failed, never left half-done.
            run.Status = RunStatus.Failed;
            run.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            run.FinishedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        run.Stage = RunStage.Scraped;
        run.PagesFetched = request.Progress.PagesFetched;
        run.IsComplete = request.Progress.IsComplete;
        run.ListingsFetched = result.Written;
        run.FinishedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        Console.WriteLine($"Staged {result.Written} listings from {code}/{set} as run {run.ScrapeRunId} " +
                          $"(pages={run.PagesFetched}, complete={run.IsComplete}, duplicates skipped={result.DuplicatesSkipped})");
        return 0;
    }
}
