using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.IntegrationTests.Infrastructure;

/// <summary>Small builder for warehouse rows with hand-chosen values, so expected statistics can be computed by hand.</summary>
public sealed class TestData(PriceRadarDbContext db)
{
    public const byte FixtureSourceId = 1;
    private int _listingCounter;

    public async Task<Book> BookAsync(string title = "Seobe", string author = "Miloš Crnjanski")
    {
        var book = new Book
        {
            Title = title,
            Author = author,
            NormTitle = Core.Normalization.TextNormalizer.NormalizeTitle(title),
            NormAuthor = Core.Normalization.TextNormalizer.NormalizeAuthor(author),
            Origin = BookOrigin.Seed,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    public async Task<ScrapeRun> RunAsync(DateTime atUtc, RunStatus status = RunStatus.Succeeded)
    {
        var run = new ScrapeRun
        {
            SourceId = FixtureSourceId,
            ScopeKey = "fixture:category:knjige",
            Status = status,
            Stage = status == RunStatus.Succeeded ? RunStage.Loaded : RunStage.Created,
            IsComplete = true,
            MaxPages = 10,
            EurRsdRate = 117.2m,
            IdempotencyKey = Guid.NewGuid(),
            CreatedAtUtc = atUtc,
            StartedAtUtc = atUtc,
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>A listing of <paramref name="book"/> observed at the given (time, price) points, one run per point.</summary>
    public async Task<Listing> ListingAsync(Book book, params (DateTime At, decimal Price)[] observations)
    {
        var ordered = observations.OrderBy(o => o.At).ToArray();
        var runs = new List<ScrapeRun>();
        foreach (var (at, _) in ordered)
        {
            runs.Add(await RunAsync(at));
        }

        var listing = new Listing
        {
            SourceId = FixtureSourceId,
            ExternalId = $"T{++_listingCounter:0000}",
            BookId = book.BookId,
            MatchStatus = MatchStatus.AutoMatched,
            Title = book.Title,
            Url = $"https://test.local/{_listingCounter}",
            ScopeKey = "fixture:category:knjige",
            Status = ListingStatus.Active,
            CurrentPriceRsd = ordered[^1].Price,
            FirstSeenAtUtc = ordered[0].At,
            LastSeenAtUtc = ordered[^1].At,
            LastSeenRunId = runs[^1].ScrapeRunId,
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        for (var i = 0; i < ordered.Length; i++)
        {
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                ListingId = listing.ListingId,
                BookId = book.BookId,
                SourceId = FixtureSourceId,
                ScrapeRunId = runs[i].ScrapeRunId,
                DateKey = DimDate.ToKey(DateOnly.FromDateTime(ordered[i].At)),
                PriceRsd = ordered[i].Price,
                CapturedAtUtc = ordered[i].At,
            });
        }

        await db.SaveChangesAsync();
        return listing;
    }

    public async Task DisappearAsync(Listing listing, DateTime atUtc)
    {
        listing.Status = ListingStatus.Disappeared;
        listing.MissedRunCount = 2;
        listing.DisappearedAtUtc = atUtc;
        await db.SaveChangesAsync();
    }
}
