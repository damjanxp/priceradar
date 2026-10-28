using PriceRadar.Core.Abstractions;
using PriceRadar.Infrastructure.Sources;
using PriceRadar.Infrastructure.Staging;

namespace PriceRadar.UnitTests.Sources;

/// <summary>The fixture files must realise the presence scenario in spec §7 exactly; DAY-3 tests depend on it.</summary>
public class FixtureScenarioTests
{
    private static readonly string Root = FixturePaths.Resolve();

    private static async Task<List<RawListing>> ReadAsync(string code, string set, string scope, ScrapeRequest? request = null)
    {
        request ??= new ScrapeRequest { SourceCode = code, ScopeKey = scope, FixtureSet = set };
        var result = new List<RawListing>();
        await foreach (var listing in new FixtureListingSource(code, Root).FetchAsync(request, CancellationToken.None))
        {
            result.Add(listing);
        }

        return result;
    }

    private static Task<List<RawListing>> FixtureRun(string set) => ReadAsync("fixture", set, "fixture:category:knjige");

    private static HashSet<string> Ids(IEnumerable<string> range) => [.. range];

    private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

    private static IEnumerable<string> L(int from, int to) => Enumerable.Range(from, to - from + 1).Select(n => $"L{n:000}");

    [Theory]
    [InlineData("run-001", 120)]
    [InlineData("run-002", 118)]
    [InlineData("run-003", 122)]
    public async Task FixtureRuns_HaveScenarioSizes(string set, int expected)
    {
        var listings = await FixtureRun(set);

        listings.Count.ShouldBe(expected);
        listings.Select(l => l.ExternalId).Distinct().Count().ShouldBe(expected);
    }

    [Fact]
    public async Task FixtureRuns_MatchPresenceScenario()
    {
        var run1 = Ids((await FixtureRun("run-001")).Select(l => l.ExternalId));
        var run2 = Ids((await FixtureRun("run-002")).Select(l => l.ExternalId));
        var run3 = Ids((await FixtureRun("run-003")).Select(l => l.ExternalId));

        run1.SetEquals(L(1, 120)).ShouldBeTrue();
        run2.SetEquals(L(1, 112).Concat(L(121, 126))).ShouldBeTrue();
        run3.SetEquals(L(1, 110).Concat(L(113, 114)).Concat(L(121, 130))).ShouldBeTrue();

        // After run-003 with N = 2: L115–L120 missed run-002 and run-003 → Disappeared.
        var disappeared = run1.Union(run2).Where(id => !run2.Contains(id) && !run3.Contains(id)).Order().ToList();
        disappeared.ShouldBe(L(115, 120).ToList());
    }

    [Fact]
    public async Task FixtureRuns_ContainSerbianSpellingVariants()
    {
        var listings = await FixtureRun("run-001");

        listings.Any(l => l.Title.Any(IsCyrillic)).ShouldBeTrue("a Cyrillic title");
        listings.ShouldContain(l => l.Author == null, "a listing without author");
        listings.ShouldContain(l => l.Price == "Dogovor", "a price that must be rejected");
    }

    [Fact]
    public async Task KpFixture_LooksLikeKupujemProdajem()
    {
        var listings = await ReadAsync("kp", "run-001", "kp:category:knjige");

        listings.Count.ShouldBe(40);
        listings.ShouldContain(l => l.Price != null && l.Price.Contains('€'), "EUR prices");
        listings.ShouldAllBe(l => l.Url.StartsWith("https://kp.priceradar.local/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fetch_ReportsCompleteProgressAndCaptureTime()
    {
        var request = new ScrapeRequest { SourceCode = "fixture", ScopeKey = "fixture:category:knjige", FixtureSet = "run-002" };

        await ReadAsync("fixture", "run-002", request.ScopeKey, request);

        request.Progress.IsComplete.ShouldBeTrue();
        request.Progress.PagesFetched.ShouldBeGreaterThan((short)0);
        request.Progress.CapturedAtUtc.ShouldBe(new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Fetch_WrongScope_Throws()
    {
        await Should.ThrowAsync<InvalidDataException>(() => ReadAsync("fixture", "run-001", "fixture:q:andric"));
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("run-001/../../x")]
    public async Task Fetch_PathTraversal_IsRejected(string set)
    {
        await Should.ThrowAsync<ArgumentException>(() => ReadAsync("fixture", set, "fixture:category:knjige"));
    }

    [Fact]
    public void PayloadHash_ChangesWhenAnyRawFieldChanges()
    {
        var a = new RawListing("L001", "Seobe", "Miloš Crnjanski", "800 din", null, "https://x/1", null);

        StagingBulkWriter.PayloadHash(a).ShouldBe(StagingBulkWriter.PayloadHash(a with { }));
        StagingBulkWriter.PayloadHash(a).ShouldNotBe(StagingBulkWriter.PayloadHash(a with { Price = "750 din" }));
        StagingBulkWriter.PayloadHash(a with { Author = "ab", Title = "c" })
            .ShouldNotBe(StagingBulkWriter.PayloadHash(a with { Author = "a", Title = "bc" }));
    }
}
