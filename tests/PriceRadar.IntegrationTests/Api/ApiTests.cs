using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceRadar.IntegrationTests.Infrastructure;

namespace PriceRadar.IntegrationTests.Api;

/// <summary>Endpoint behaviour from spec §8.2–8.4: status codes, response shapes, ProblemDetails, API key, rate limit.</summary>
[Collection(SqlCollection.Name)]
public sealed class ApiTests(MsSqlFixture sql) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await sql.ResetAsync();
        _factory = new ApiFactory(sql);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>Andrić with four listings at 100–400 RSD seen two days ago: P25 175, median 250, P75 325.</summary>
    private async Task<int> SeedAndricAsync()
    {
        await using var db = sql.CreateDbContext();
        var data = new TestData(db);
        var book = await data.BookAsync("Na Drini ćuprija", "Ivo Andrić");
        foreach (var price in new[] { 100m, 200m, 300m, 400m })
        {
            await data.ListingAsync(book, (DateTime.UtcNow.AddDays(-2), price));
        }

        return book.BookId;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Theory]
    [InlineData(150, "cheap")]
    [InlineData(250, "fair")]
    [InlineData(325, "expensive")]
    public async Task Lookup_KnownBook_ReturnsVerdictWithStats(int asking, string verdict)
    {
        var bookId = await SeedAndricAsync();

        // Cyrillic, upper case and no diacritics all resolve to the same canonical book.
        var response = await _client.GetAsync($"/api/lookup?title=НА ДРИНИ ЋУПРИЈА&author=Ivo Andric&askingPrice={asking}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("verdict").GetString().ShouldBe(verdict);
        body.GetProperty("match").GetProperty("status").GetString().ShouldBe("matched");
        body.GetProperty("match").GetProperty("bookId").GetInt32().ShouldBe(bookId);
        var stats = body.GetProperty("stats");
        stats.GetProperty("sampleSize").GetInt32().ShouldBe(4);
        (stats.GetProperty("p25").GetDecimal(), stats.GetProperty("p75").GetDecimal()).ShouldBe((175m, 325m));
    }

    [Fact]
    public async Task Lookup_TitleOnlyWithTwoAuthors_IsAmbiguousWithCandidates()
    {
        await using (var db = sql.CreateDbContext())
        {
            var data = new TestData(db);
            await data.BookAsync("Pesme", "Desanka Maksimović");
            await data.BookAsync("Pesme", "Branko Radičević");
        }

        var body = await JsonAsync(await _client.GetAsync("/api/lookup?title=Pesme&askingPrice=300"));

        body.GetProperty("match").GetProperty("status").GetString().ShouldBe("ambiguous");
        body.GetProperty("match").GetProperty("candidates").GetArrayLength().ShouldBe(2);
        body.GetProperty("verdict").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Lookup_UnknownBook_IsNoMatchWithUnknownVerdict()
    {
        var body = await JsonAsync(await _client.GetAsync("/api/lookup?title=Knjiga koje nema&askingPrice=300"));

        body.GetProperty("match").GetProperty("status").GetString().ShouldBe("no_match");
        body.GetProperty("verdict").GetString().ShouldBe("unknown");
    }

    [Fact]
    public async Task Lookup_InvalidParameters_Return400ProblemDetails()
    {
        var response = await _client.GetAsync("/api/lookup?title=x&askingPrice=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await JsonAsync(response);
        body.GetProperty("type").GetString().ShouldBe("https://priceradar.dev/problems/validation");
        body.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        var errors = body.GetProperty("errors");
        errors.TryGetProperty("title", out _).ShouldBeTrue();
        errors.TryGetProperty("askingPrice", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Books_UnknownId_Returns404ProblemWithTraceId()
    {
        var response = await _client.GetAsync("/api/books/99999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await JsonAsync(response);
        body.GetProperty("type").GetString().ShouldBe("https://priceradar.dev/problems/not-found");
        body.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Catalog_ListsBooksWithCurrentPricesQuantitiesAndTotals()
    {
        int andric;
        await using (var db = sql.CreateDbContext())
        {
            var data = new TestData(db);   // one instance: it numbers the listings' external ids
            var drina = await data.BookAsync("Na Drini ćuprija", "Ivo Andrić");
            foreach (var price in new[] { 100m, 200m, 300m, 400m })
            {
                await data.ListingAsync(drina, (DateTime.UtcNow.AddDays(-2), price));
            }

            andric = drina.BookId;
            var seobe = await data.BookAsync("Seobe", "Miloš Crnjanski");
            await data.ListingAsync(seobe, (DateTime.UtcNow.AddDays(-1), 900m));
            var gone = await data.ListingAsync(seobe, (DateTime.UtcNow.AddDays(-5), 700m));
            await data.DisappearAsync(gone, DateTime.UtcNow.AddDays(-3));
            await data.BookAsync("Pesme", "Desanka Maksimović");   // nothing ever listed
        }

        var page = await JsonAsync(await _client.GetAsync("/api/catalog"));

        page.GetProperty("totalCount").GetInt32().ShouldBe(2);   // in stock only by default
        var first = page.GetProperty("items")[0];
        first.GetProperty("bookId").GetInt32().ShouldBe(andric);   // default order: most copies on sale
        first.GetProperty("activeListings").GetInt32().ShouldBe(4);
        (first.GetProperty("minPrice").GetDecimal(), first.GetProperty("medianPrice").GetDecimal(), first.GetProperty("maxPrice").GetDecimal())
            .ShouldBe((100m, 250m, 400m));
        first.GetProperty("sources").GetString().ShouldBe("fixture");

        var seobeRow = page.GetProperty("items")[1];
        // The disappeared copy counts as gone and is left out of the current prices.
        (seobeRow.GetProperty("activeListings").GetInt32(), seobeRow.GetProperty("disappearedListings").GetInt32()).ShouldBe((1, 1));
        seobeRow.GetProperty("medianPrice").GetDecimal().ShouldBe(900m);

        var totals = page.GetProperty("totals");
        (totals.GetProperty("books").GetInt32(), totals.GetProperty("booksInStock").GetInt32(),
            totals.GetProperty("activeListings").GetInt32(), totals.GetProperty("disappearedListings").GetInt32()).ShouldBe((3, 2, 5, 1));

        var all = await JsonAsync(await _client.GetAsync("/api/catalog?inStock=false&sort=-median"));
        all.GetProperty("totalCount").GetInt32().ShouldBe(3);
        var titles = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        titles.ShouldBe(["Seobe", "Na Drini ćuprija", "Pesme"]);   // books without a price sort last

        var search = await JsonAsync(await _client.GetAsync("/api/catalog?q=ЦРЊАНСКИ"));
        search.GetProperty("items").GetArrayLength().ShouldBe(1);
        search.GetProperty("items")[0].GetProperty("title").GetString().ShouldBe("Seobe");

        // A page past the end is empty but still reports the total.
        var beyond = await JsonAsync(await _client.GetAsync("/api/catalog?page=5&pageSize=1"));
        (beyond.GetProperty("items").GetArrayLength(), beyond.GetProperty("totalCount").GetInt32()).ShouldBe((0, 2));
    }

    [Fact]
    public async Task Catalog_InvalidSort_Returns400ProblemDetails()
    {
        var response = await _client.GetAsync("/api/catalog?sort=price;drop");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty("sort", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Books_SearchStatsListingsAndHistory_ReturnSpecShapes()
    {
        var bookId = await SeedAndricAsync();

        var list = await JsonAsync(await _client.GetAsync("/api/books?q=drini&pageSize=5"));
        list.GetProperty("totalCount").GetInt32().ShouldBe(1);
        list.GetProperty("items")[0].GetProperty("medianPrice").GetDecimal().ShouldBe(250m);

        // Author search, in Cyrillic, on the first word of the author key ("andric ivo").
        var byAuthor = await JsonAsync(await _client.GetAsync("/api/books?q=АНДРИЋ"));
        byAuthor.GetProperty("totalCount").GetInt32().ShouldBe(1);

        var detail = await JsonAsync(await _client.GetAsync($"/api/books/{bookId}"));
        detail.GetProperty("sources")[0].GetProperty("source").GetString().ShouldBe("fixture");
        detail.GetProperty("sources")[0].GetProperty("activeListings").GetInt32().ShouldBe(4);

        var stats = await JsonAsync(await _client.GetAsync($"/api/books/{bookId}/stats?windowDays=30"));
        (stats.GetProperty("currency").GetString(), stats.GetProperty("median").GetDecimal()).ShouldBe(("RSD", 250m));

        var listings = await JsonAsync(await _client.GetAsync($"/api/books/{bookId}/listings?sort=-price"));
        listings.GetProperty("items")[0].GetProperty("priceRsd").GetDecimal().ShouldBe(400m);

        var history = await JsonAsync(await _client.GetAsync($"/api/books/{bookId}/price-history?bucket=day"));
        history.GetArrayLength().ShouldBe(1);
        history[0].GetProperty("snapshots").GetInt32().ShouldBe(4);
    }

    [Theory]
    [InlineData("from=2026-10-10&to=2026-10-01", "from")]
    [InlineData("from=2025-01-01&to=2026-10-01", "to")]
    [InlineData("bucket=year", "bucket")]
    public async Task PriceHistory_InvalidRange_Returns400(string query, string field)
    {
        var bookId = await SeedAndricAsync();

        var response = await _client.GetAsync($"/api/books/{bookId}/price-history?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task IngestionPost_WithoutApiKey_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/ingestion/runs", new { source = "fixture", fixtureSet = "run-001" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(response)).GetProperty("type").GetString().ShouldBe("https://priceradar.dev/problems/unauthorized");
    }

    [Fact]
    public async Task IngestionPost_WithApiKey_Returns202ThenConflictWhileActive()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ApiKey);

        var first = await _client.PostAsJsonAsync("/api/ingestion/runs", new { source = "fixture", fixtureSet = "run-001" });
        var second = await _client.PostAsJsonAsync("/api/ingestion/runs", new { source = "fixture", fixtureSet = "run-002" });

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        first.Headers.Location!.ToString().ShouldContain("/api/ingestion/runs/");
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);   // no Worker in tests: the first run stays Queued
        (await JsonAsync(second)).GetProperty("activeRunId").GetInt64().ShouldBe((await JsonAsync(first)).GetProperty("runId").GetInt64());
    }

    [Fact]
    public async Task IngestionPost_InvalidBody_UsesJsonFieldNames()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ApiKey);

        var response = await _client.PostAsJsonAsync("/api/ingestion/runs", new { source = "fixture", fixtureSet = "../x", maxPages = 99 });

        var errors = (await JsonAsync(response)).GetProperty("errors");
        errors.TryGetProperty("maxPages", out _).ShouldBeTrue();
        errors.TryGetProperty("fixtureSet", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Lookup_OverRateLimit_Returns429WithRetryAfter()
    {
        await using var limited = new ApiFactory(sql, lookupPerMinute: 2);
        using var client = limited.CreateClient();

        var codes = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            last = await client.GetAsync("/api/lookup?title=Seobe&askingPrice=100");
            codes.Add(last.StatusCode);
        }

        codes.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
        last!.Headers.RetryAfter.ShouldNotBeNull();
        (await JsonAsync(last)).GetProperty("type").GetString().ShouldBe("https://priceradar.dev/problems/rate-limited");
    }

    [Fact]
    public async Task Responses_EchoCorrelationId_AndProblemTraceIdMatchesIt()
    {
        var mine = new HttpRequestMessage(HttpMethod.Get, "/health");
        mine.Headers.Add("X-Correlation-Id", "market-stall-42");
        (await _client.SendAsync(mine)).Headers.GetValues("X-Correlation-Id").Single().ShouldBe("market-stall-42");

        // Without one, the id is the W3C trace id, which is also inside the ProblemDetails traceId.
        var problem = await _client.GetAsync("/api/books/99999999");
        var correlation = problem.Headers.GetValues("X-Correlation-Id").Single();
        (await JsonAsync(problem)).GetProperty("traceId").GetString()!.ShouldContain(correlation);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("default-src 'self'");
    }
}
