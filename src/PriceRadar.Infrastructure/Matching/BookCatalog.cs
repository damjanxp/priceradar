using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PriceRadar.Core.Matching;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Matching;

/// <summary>All canonical books as a <see cref="CandidateIndex"/>, plus the author keys the normaliser uses to split
/// "Author - Title" ads.</summary>
public sealed record BookCatalog(CandidateIndex Index, IReadOnlySet<string> AuthorKeys)
{
    public static async Task<BookCatalog> LoadAsync(PriceRadarDbContext db, CancellationToken ct)
    {
        var books = await db.Books.AsNoTracking()
            .Select(b => new BookCandidate(b.BookId, b.NormTitle, b.NormAuthor, b.Title, b.Author))
            .ToListAsync(ct);
        return new BookCatalog(new CandidateIndex(books),
            books.Where(b => b.NormAuthor.Length > 0).Select(b => b.NormAuthor).ToHashSet(StringComparer.Ordinal));
    }
}

/// <summary>
/// Keeps one catalogue in memory for the lookup endpoint and rebuilds it when books were added (checked at most every
/// 60 s), so a lookup never pays for building the index. Jobs load a fresh catalogue per run instead.
/// </summary>
public sealed class BookCatalogProvider(IServiceScopeFactory scopes, TimeProvider clock)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (BookCatalog Catalog, int MaxBookId, int Count)? _current;
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    public async Task<BookCatalog> GetAsync(CancellationToken ct)
    {
        if (_current is { } c && clock.GetUtcNow() - _checkedAt < CheckInterval)
        {
            return c.Catalog;
        }

        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PriceRadarDbContext>();
            var version = await db.Books.GroupBy(_ => 1).Select(g => new { Max = g.Max(b => b.BookId), Count = g.Count() }).SingleOrDefaultAsync(ct);
            var (max, count) = (version?.Max ?? 0, version?.Count ?? 0);

            if (_current is not { } cur || cur.MaxBookId != max || cur.Count != count)
            {
                _current = (await BookCatalog.LoadAsync(db, ct), max, count);
            }

            _checkedAt = clock.GetUtcNow();
            return _current.Value.Catalog;
        }
        finally
        {
            _gate.Release();
        }
    }
}
