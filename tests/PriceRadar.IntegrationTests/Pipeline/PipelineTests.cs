using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PriceRadar.Core.Domain;
using PriceRadar.IntegrationTests.Api;
using PriceRadar.IntegrationTests.Infrastructure;

namespace PriceRadar.IntegrationTests.Pipeline;

/// <summary>The real Scrape → Match → Resolve → Load chain on the fixture files (spec §7, §6.5–6.6, FR-8 – FR-11).</summary>
[Collection(SqlCollection.Name)]
public sealed class PipelineTests(MsSqlFixture sql) : IAsyncLifetime
{
    private readonly PipelineHarness _pipeline = new(sql);

    public Task InitializeAsync() => sql.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>tests/fixtures/sources/kp/test-ambiguous.json: 4 author-less ads in 2 groups, 1 exact match, 1 new book.</summary>
    private const string Ambiguous = "test-ambiguous";

    private static FakeAiResolver NeverCalled() => new(_ => throw new InvalidOperationException("Claude must not be called"));

    [Fact]
    public async Task FixtureRuns001To003_Give121ActiveAnd6Disappeared()
    {
        _pipeline.Ai.ApiKey = "";   // AI off: ambiguous rows stay Pending and must not disturb presence tracking

        foreach (var set in new[] { "run-001", "run-002", "run-003" })
        {
            await _pipeline.RunAsync("fixture", set, NeverCalled());
        }

        await using var db = sql.CreateDbContext();
        var states = await db.Listings.Where(l => l.SourceId == 1)
            .GroupBy(l => new { l.Status, l.MissedRunCount }).Select(g => new { g.Key.Status, g.Key.MissedRunCount, Count = g.Count() })
            .ToListAsync();
        states.Single(s => s.Status == ListingStatus.Disappeared).Count.ShouldBe(6);
        states.Where(s => s.Status == ListingStatus.Active).Sum(s => s.Count).ShouldBe(121);
        states.Single(s => s is { Status: ListingStatus.Active, MissedRunCount: 1 }).Count.ShouldBe(2);

        (await db.Listings.Where(l => l.Status == ListingStatus.Disappeared).Select(l => l.ExternalId).OrderBy(x => x).ToListAsync())
            .ShouldBe(["L115", "L116", "L117", "L118", "L119", "L120"]);
        (await db.PriceSnapshots.GroupBy(p => new { p.ListingId, p.ScrapeRunId }).AnyAsync(g => g.Count() > 1)).ShouldBeFalse();
    }

    [Fact]
    public async Task LoadJob_Rerun_AddsNothing()
    {
        _pipeline.Ai.ApiKey = "";
        var runId = await _pipeline.RunAsync("fixture", "run-001", NeverCalled());
        await using var before = sql.CreateDbContext();
        var snapshots = await before.PriceSnapshots.CountAsync();

        await using (var db = sql.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("EXEC dw.usp_LoadRun @p0", runId);   // bypass the Stage guard on purpose
        }

        await using var after = sql.CreateDbContext();
        (await after.PriceSnapshots.CountAsync()).ShouldBe(snapshots);
    }

    [Fact]
    public async Task AmbiguousListings_AreResolvedOncePerString_ThenServedFromCache()
    {
        var andric = await SeedAndricAsync();
        var claude = new FakeAiResolver(r => FakeAiResolver.Answer(MatchDecision.Match, r.Candidates[0].BookId));

        var first = await _pipeline.RunAsync("kp", Ambiguous, claude);
        var second = await _pipeline.RunAsync("kp", Ambiguous, claude);   // the same ads again

        // 4 ambiguous ads, but only 2 distinct normalised strings: 2 calls, and none on the second run (FR-8).
        claude.Calls.ShouldBe(2);
        await using var db = sql.CreateDbContext();
        (await db.ScrapeRuns.Where(r => r.ScrapeRunId == first).Select(r => r.AiCalls).SingleAsync()).ShouldBe(2);
        (await db.ScrapeRuns.Where(r => r.ScrapeRunId == second).Select(r => r.AiCalls).SingleAsync()).ShouldBe(0);
        (await db.MatchCache.CountAsync()).ShouldBe(2);
        (await db.MatchCache.SumAsync(e => e.HitCount)).ShouldBe(2);

        (await db.Listings.CountAsync(l => l.BookId == andric && l.MatchStatus == MatchStatus.AiMatched)).ShouldBe(3);
        (await db.Listings.CountAsync(l => l.MatchStatus == MatchStatus.Pending)).ShouldBe(0);
        (await db.Books.CountAsync()).ShouldBe(3);   // the two seeded books + "Hazarski rečnik"; no duplicate Andrić books
    }

    [Fact]
    public async Task AiInactive_LeavesAmbiguousListingsPending()
    {
        await SeedAndricAsync();
        _pipeline.Ai.ApiKey = "";

        var runId = await _pipeline.RunAsync("kp", Ambiguous, NeverCalled());

        await using var db = sql.CreateDbContext();
        (await db.Listings.CountAsync(l => l.MatchStatus == MatchStatus.Pending && l.BookId == null)).ShouldBe(4);
        (await db.ScrapeRuns.SingleAsync(r => r.ScrapeRunId == runId)).Status.ShouldBe(RunStatus.Succeeded);
    }

    [Fact]
    public async Task BudgetOfZeroCalls_LeavesListingsPending()
    {
        await SeedAndricAsync();
        _pipeline.Ai.MaxCallsPerRun = 0;

        await _pipeline.RunAsync("kp", Ambiguous, NeverCalled());

        await using var db = sql.CreateDbContext();
        (await db.Listings.CountAsync(l => l.MatchStatus == MatchStatus.Pending)).ShouldBe(4);
        (await db.MatchCache.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UnsureAnswer_CreatesABookThatNeedsReview()
    {
        await SeedAndricAsync();
        var claude = new FakeAiResolver(_ => FakeAiResolver.Answer(MatchDecision.Unsure));

        await _pipeline.RunAsync("kp", Ambiguous, claude);

        await using var db = sql.CreateDbContext();
        (await db.Books.CountAsync(b => b.Origin == BookOrigin.Ai && b.NeedsReview)).ShouldBe(2);   // one per ambiguous string
        (await db.Listings.CountAsync(l => l.MatchStatus == MatchStatus.Pending)).ShouldBe(0);
    }

    [Fact]
    public async Task Lookup_AmbiguousQuery_IsAnsweredFromTheCache()
    {
        var andric = await SeedAndricAsync();
        await using var factory = new ApiFactory(sql);
        using var client = factory.CreateClient();
        var before = await client.GetFromJsonAsync<JsonElement>("/api/lookup?title=На Дрини ћуприја&askingPrice=500");

        await _pipeline.RunAsync("kp", Ambiguous, new FakeAiResolver(r => FakeAiResolver.Answer(MatchDecision.Match, andric)));
        var after = await client.GetFromJsonAsync<JsonElement>("/api/lookup?title=На Дрини ћуприја&askingPrice=500");

        before.GetProperty("match").GetProperty("status").GetString().ShouldBe("ambiguous");   // no author: never auto
        after.GetProperty("match").GetProperty("status").GetString().ShouldBe("matched");
        after.GetProperty("match").GetProperty("method").GetString().ShouldBe("ai-cache");
        after.GetProperty("match").GetProperty("bookId").GetInt32().ShouldBe(andric);
    }

    private async Task<int> SeedAndricAsync()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync("Na Drini ćuprija", "Ivo Andrić");
        await data.BookAsync("Prokleta avlija", "Ivo Andrić");
        return book.BookId;
    }
}
