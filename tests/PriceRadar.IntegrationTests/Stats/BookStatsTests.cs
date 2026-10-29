using Microsoft.Data.SqlClient;
using PriceRadar.Infrastructure.Queries;
using PriceRadar.Infrastructure.Staging;
using PriceRadar.IntegrationTests.Infrastructure;

namespace PriceRadar.IntegrationTests.Stats;

/// <summary>dw.usp_GetBookStats / dw.usp_GetPriceHistory against hand-computed values (spec §11.2, DAY-4).</summary>
[Collection(SqlCollection.Name)]
public sealed class BookStatsTests(MsSqlFixture sql) : IAsyncLifetime
{
    private static readonly DateTime AsOf = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private BookStatsQuery _query = null!;

    public async Task InitializeAsync()
    {
        await sql.ResetAsync();
        _query = new BookStatsQuery(new StagingConnectionString(sql.ConnectionString), TimeProvider.System);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static DateTime DaysAgo(double days) => AsOf.AddDays(-days);

    [Fact]
    public async Task Stats_FourListings_MatchHandComputedPercentiles()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync();
        foreach (var price in new[] { 100m, 200m, 300m, 400m })
        {
            await data.ListingAsync(book, (DaysAgo(3), price));
        }

        var stats = await _query.GetStatsAsync(book.BookId, 30, AsOf);

        // PERCENTILE_CONT position = p·(n−1): P25 = 100 + 0.75·100, median = 250, P75 = 300 + 0.25·100.
        stats.SampleSize.ShouldBe(4);
        stats.Min.ShouldBe(100m);
        stats.P25.ShouldBe(175m);
        stats.Median.ShouldBe(250m);
        stats.P75.ShouldBe(325m);
        stats.Max.ShouldBe(400m);
    }

    [Fact]
    public async Task Stats_FiveListings_OddCountHitsExactValues()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync();
        foreach (var price in new[] { 500m, 100m, 400m, 200m, 300m })
        {
            await data.ListingAsync(book, (DaysAgo(1), price));
        }

        var stats = await _query.GetStatsAsync(book.BookId, 30, AsOf);

        (stats.P25, stats.Median, stats.P75).ShouldBe((200m, 300m, 400m));
    }

    [Fact]
    public async Task Stats_ListingSeenInSeveralRuns_CountsOnceAtLatestPrice()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync();
        await data.ListingAsync(book, (DaysAgo(21), 500m), (DaysAgo(14), 450m), (DaysAgo(7), 400m));
        await data.ListingAsync(book, (DaysAgo(2), 600m));

        var stats = await _query.GetStatsAsync(book.BookId, 30, AsOf);

        stats.SampleSize.ShouldBe(2);
        stats.Min.ShouldBe(400m);      // the latest price of the first listing, not its 500 or 450
        stats.Median.ShouldBe(500m);
        stats.Max.ShouldBe(600m);
    }

    [Fact]
    public async Task Stats_WindowBoundary_ExcludesOlderSightings()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync();
        await data.ListingAsync(book, (DaysAgo(31), 900m));
        await data.ListingAsync(book, (DaysAgo(29.9), 300m));
        await data.ListingAsync(book, (AsOf.AddHours(1), 50m));   // after the as-of time: ignored

        var stats = await _query.GetStatsAsync(book.BookId, 30, AsOf);

        stats.SampleSize.ShouldBe(1);
        stats.Median.ShouldBe(300m);
    }

    [Fact]
    public async Task Stats_NoListings_ReturnsEmptyDistribution()
    {
        await using var db = sql.CreateDbContext();
        var book = await new TestData(db).BookAsync();

        var stats = await _query.GetStatsAsync(book.BookId, 30, AsOf);

        stats.SampleSize.ShouldBe(0);
        stats.Median.ShouldBeNull();
        stats.ActiveListings.ShouldBe(0);
        stats.DisappearedInWindow.ShouldBe(0);
    }

    [Fact]
    public async Task Stats_SupplyAndSoldProxy_CountOnlyTheWindow()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync();
        await data.ListingAsync(book, (DaysAgo(2), 300m));
        await data.ListingAsync(book, (DaysAgo(3), 350m));
        await data.DisappearAsync(await data.ListingAsync(book, (DaysAgo(20), 400m)), DaysAgo(6));
        await data.DisappearAsync(await data.ListingAsync(book, (DaysAgo(80), 500m)), DaysAgo(60));

        var stats = await _query.GetStatsAsync(book.BookId, 30, AsOf);

        stats.ActiveListings.ShouldBe(2);
        stats.DisappearedInWindow.ShouldBe(1);
        stats.SampleSize.ShouldBe(3);   // the disappeared listing was still seen inside the window
    }

    [Fact]
    public async Task PriceHistory_WeekBuckets_GroupByIsoWeek()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync();
        var monday = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);   // ISO week 42
        await data.ListingAsync(book, (monday, 300m), (monday.AddDays(7), 280m));
        await data.ListingAsync(book, (monday.AddDays(2), 500m));
        await data.ListingAsync(book, (monday.AddDays(6), 400m));             // Sunday, still week 42

        var weeks = await _query.GetPriceHistoryAsync(book.BookId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "week");

        weeks.Count.ShouldBe(2);
        weeks[0].ShouldBe(new PricePoint(new DateTime(2026, 10, 12), 300m, 400m, 500m, 3));
        weeks[1].ShouldBe(new PricePoint(new DateTime(2026, 10, 19), 280m, 280m, 280m, 1));
    }

    [Fact]
    public async Task PriceHistory_UnknownBucket_IsRejectedBySql()
    {
        var ex = await Should.ThrowAsync<SqlException>(() =>
            _query.GetPriceHistoryAsync(1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "year"));

        ex.Number.ShouldBe(50002);
    }
}
