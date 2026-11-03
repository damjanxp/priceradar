using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Ai;
using PriceRadar.Infrastructure.Jobs;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Sources;
using PriceRadar.Infrastructure.Staging;
using PriceRadar.IntegrationTests.Infrastructure;
using static Microsoft.Extensions.Options.Options;

namespace PriceRadar.IntegrationTests.Pipeline;

/// <summary>Answers like Claude would, counts calls, never touches the network.</summary>
public sealed class FakeAiResolver(Func<AiMatchRequest, AiMatchAnswer> answer) : IAiMatchResolver
{
    public int Calls { get; private set; }

    public Task<AiMatchAnswer> ResolveAsync(AiMatchRequest request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(answer(request));
    }

    public static AiMatchAnswer Answer(MatchDecision decision, int? bookId = null, decimal confidence = 0.9m) =>
        new(decision, bookId, confidence, "fake", "fake-model", 600, 150, 5);
}

/// <summary>Runs the real pipeline jobs in-process against the Testcontainers database (one DbContext per job, as Hangfire would).</summary>
public sealed class PipelineHarness(MsSqlFixture sql)
{
    public AiSettings Ai { get; } = new() { ApiKey = "test-key" };

    public async Task<long> RunAsync(string source, string fixtureSet, IAiMatchResolver resolver)
    {
        long runId;
        await using (var db = sql.CreateDbContext())
        {
            var sourceId = await db.Sources.Where(s => s.Code == source).Select(s => s.SourceId).SingleAsync();
            var run = new ScrapeRun
            {
                SourceId = sourceId,
                ScopeKey = $"{source}:category:knjige",
                FixtureSet = fixtureSet,
                Status = RunStatus.Queued,
                MaxPages = 10,
                EurRsdRate = 117.2m,
                IdempotencyKey = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.ScrapeRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.ScrapeRunId;
        }

        await using (var db = sql.CreateDbContext())
        {
            var writer = new StagingBulkWriter(new StagingConnectionString(sql.ConnectionString), NullLogger<StagingBulkWriter>.Instance);
            var root = FixturePaths.Resolve();
            var sources = new ListingSourceRegistry([new FixtureListingSource("fixture", root), new FixtureListingSource("kp", root)]);
            await new ScrapeJob(db, sources, writer, TimeProvider.System, NullLogger<ScrapeJob>.Instance).ExecuteAsync(runId, CancellationToken.None);
        }

        await using (var db = sql.CreateDbContext())
        {
            await new MatchJob(db, Create(new PricingSettings()), Create(new MatchingSettings()), NullLogger<MatchJob>.Instance)
                .ExecuteAsync(runId, CancellationToken.None);
        }

        await ResolveAsync(runId, resolver);

        await using (var db = sql.CreateDbContext())
        {
            await new LoadJob(db, Create(new SoldSettings()), TimeProvider.System, NullLogger<LoadJob>.Instance).ExecuteAsync(runId, CancellationToken.None);
        }

        return runId;
    }

    public async Task ResolveAsync(long runId, IAiMatchResolver resolver)
    {
        await using var db = sql.CreateDbContext();
        await new ResolveJob(db, resolver, new MatchCacheService(db, TimeProvider.System), Create(Ai), Create(new MatchingSettings()),
            NullLogger<ResolveJob>.Instance).ExecuteAsync(runId, CancellationToken.None);
    }
}
