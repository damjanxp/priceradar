using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceRadar.Api.Problems;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Persistence;
using PriceRadar.Infrastructure.Queries;

namespace PriceRadar.Api.Controllers;

[ApiController]
[Route("api/books")]
public sealed class BooksController(PriceRadarDbContext db, BookSearchQuery search, BookStatsQuery stats, TimeProvider clock) : ControllerBase
{
    private const int MaxHistoryDays = 365;

    /// <summary>Lists or searches canonical books (FR-12). <c>q</c> is normalised like titles (Cyrillic, diacritics).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<BookSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BookSummary>>> List(
        [FromQuery, StringLength(100, MinimumLength = 2)] string? q,
        [FromQuery, Range(1, 100_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery, RegularExpression("^(title|-listingCount|-lastSeen)$", ErrorMessage = "sort must be one of: title, -listingCount, -lastSeen.")]
        string sort = "title",
        CancellationToken ct = default)
    {
        var order = sort switch
        {
            "-listingCount" => BookSort.ListingCountDesc,
            "-lastSeen" => BookSort.LastSeenDesc,
            _ => BookSort.Title,
        };
        var (items, total) = await search.SearchAsync(q, page, pageSize, order, ct);
        return new PagedResult<BookSummary>(items, page, pageSize, total);
    }

    /// <summary>One canonical book with the sources it has listings on.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<BookDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([Range(1, int.MaxValue)] int id, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(b => b.BookId == id, ct);
        if (book is null)
        {
            return BookNotFound(id);
        }

        // Group on the FK column: EF cannot translate GROUP BY over a navigation property (l.Source.Code).
        var codes = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.SourceId, s => s.Code, ct);
        var sources = (await db.Listings.AsNoTracking()
                .Where(l => l.BookId == id)
                .GroupBy(l => l.SourceId)
                .Select(g => new { SourceId = g.Key, Active = g.Count(l => l.Status == ListingStatus.Active), Total = g.Count() })
                .ToListAsync(ct))
            .Select(g => new BookSource(codes[g.SourceId], g.Active, g.Total))
            .OrderBy(s => s.Source)
            .ToList();

        return Ok(new BookDetail(book.BookId, book.Title, book.Author, book.Origin, book.NeedsReview, book.CreatedAtUtc, sources));
    }

    /// <summary>Supply, price distribution and the disappeared-listing sold proxy for a window (FR-13).</summary>
    [HttpGet("{id:int}/stats")]
    [ProducesResponseType<BookStatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stats([Range(1, int.MaxValue)] int id, [FromQuery, Range(1, 365)] int windowDays = 30, CancellationToken ct = default)
    {
        if (!await db.Books.AnyAsync(b => b.BookId == id, ct))
        {
            return BookNotFound(id);
        }

        return Ok(BookStatsDto.From(await stats.GetStatsAsync(id, windowDays, ct: ct)));
    }

    /// <summary>Listings of a book (FR-14).</summary>
    [HttpGet("{id:int}/listings")]
    [ProducesResponseType<PagedResult<ListingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Listings(
        [Range(1, int.MaxValue)] int id,
        [FromQuery, RegularExpression("^(active|disappeared|all)$", ErrorMessage = "status must be one of: active, disappeared, all.")]
        string status = "active",
        [FromQuery, Range(1, 100_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery, RegularExpression("^(price|-price|-lastSeen)$", ErrorMessage = "sort must be one of: price, -price, -lastSeen.")]
        string sort = "price",
        CancellationToken ct = default)
    {
        if (!await db.Books.AnyAsync(b => b.BookId == id, ct))
        {
            return BookNotFound(id);
        }

        var query = db.Listings.AsNoTracking().Where(l => l.BookId == id);
        query = status switch
        {
            "active" => query.Where(l => l.Status == ListingStatus.Active),
            "disappeared" => query.Where(l => l.Status == ListingStatus.Disappeared),
            _ => query,
        };
        query = sort switch
        {
            "-price" => query.OrderByDescending(l => l.CurrentPriceRsd).ThenBy(l => l.ListingId),
            "-lastSeen" => query.OrderByDescending(l => l.LastSeenAtUtc).ThenBy(l => l.ListingId),
            _ => query.OrderBy(l => l.CurrentPriceRsd).ThenBy(l => l.ListingId),
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new ListingDto(l.ListingId, l.Source!.Code, l.Title, l.Condition, l.CurrentPriceRsd, l.Status,
                l.FirstSeenAtUtc, l.LastSeenAtUtc, l.DisappearedAtUtc, l.Url, l.MatchStatus, l.MatchScore))
            .ToListAsync(ct);
        return Ok(new PagedResult<ListingDto>(items, page, pageSize, total));
    }

    /// <summary>Min / median / max per day or ISO week (FR-14). Defaults to the last 90 days; at most 365 days.</summary>
    [HttpGet("{id:int}/price-history")]
    [ProducesResponseType<IReadOnlyList<PricePoint>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PriceHistory(
        [Range(1, int.MaxValue)] int id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery, RegularExpression("^(day|week)$", ErrorMessage = "bucket must be 'day' or 'week'.")] string bucket = "day",
        CancellationToken ct = default)
    {
        var end = to ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var start = from ?? end.AddDays(-90);
        if (start > end)
        {
            ModelState.AddModelError("from", "from must not be after to.");
        }
        else if (end.DayNumber - start.DayNumber > MaxHistoryDays)
        {
            ModelState.AddModelError("to", $"The range from..to may span at most {MaxHistoryDays} days.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!await db.Books.AnyAsync(b => b.BookId == id, ct))
        {
            return BookNotFound(id);
        }

        return Ok(await stats.GetPriceHistoryAsync(id, start, end, bucket, ct));
    }

    private NotFoundObjectResult BookNotFound(int id) =>
        NotFound(ProblemTypes.Create(HttpContext, 404, ProblemTypes.NotFound, "Book not found.", $"No book with id {id}."));
}

public sealed record BookSource(string Source, int ActiveListings, int TotalListings);

public sealed record BookDetail(int BookId, string Title, string? Author, BookOrigin Origin, bool NeedsReview, DateTime CreatedAtUtc,
    IReadOnlyList<BookSource> Sources);

public sealed record ListingDto(long ListingId, string Source, string Title, string? Condition, decimal PriceRsd, ListingStatus Status,
    DateTime FirstSeenAtUtc, DateTime LastSeenAtUtc, DateTime? DisappearedAtUtc, string Url, MatchStatus MatchStatus, decimal? MatchScore);

public sealed record BookStatsDto(
    int BookId, int WindowDays, DateTime AsOfUtc, string Currency, int SampleSize, int ActiveListings, int DisappearedInWindow,
    decimal? Min, decimal? P25, decimal? Median, decimal? P75, decimal? Max, IReadOnlyList<string> Notes)
{
    public static BookStatsDto From(BookStats s) => new(
        s.BookId, s.WindowDays, s.AsOfUtc, "RSD", s.SampleSize, s.ActiveListings, s.DisappearedInWindow,
        s.Min, s.P25, s.Median, s.P75, s.Max,
        ["disappearedInWindow is a sold proxy (listings gone for 2 complete runs), not confirmed sales"]);
}
