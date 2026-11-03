using Dapper;
using Microsoft.Data.SqlClient;
using PriceRadar.Core.Normalization;
using PriceRadar.Infrastructure.Staging;

namespace PriceRadar.Infrastructure.Queries;

public sealed record BookSummary(int BookId, string Title, string? Author, int ActiveListings, decimal? MedianPrice, DateTime? LastSeenAtUtc);

public enum BookSort
{
    Title,
    ListingCountDesc,
    LastSeenDesc,
}

/// <summary>
/// Book list and search for GET /api/books (FR-12). The query text goes through the same normaliser as titles and must
/// match the start of a word in the title or the author key ("drini", "andric", "АНДРИЋ").
/// </summary>
public sealed class BookSearchQuery(StagingConnectionString connectionString)
{
    // Allow-listed ORDER BY fragments: user input never reaches the SQL text (spec §10.3).
    private static readonly Dictionary<BookSort, string> OrderBy = new()
    {
        [BookSort.Title] = "b.Title, b.BookId",
        [BookSort.ListingCountDesc] = "ActiveListings DESC, b.BookId",
        [BookSort.LastSeenDesc] = "LastSeenAtUtc DESC, b.BookId",
    };

    public async Task<(IReadOnlyList<BookSummary> Items, int TotalCount)> SearchAsync(
        string? q, int page, int pageSize, BookSort sort, CancellationToken ct = default)
    {
        var norm = string.IsNullOrWhiteSpace(q) ? null : TextNormalizer.NormalizeTitle(q);
        var sql = $"""
            SELECT COUNT(*) FROM dw.Book AS b
            WHERE @Norm IS NULL OR (' ' + b.NormTitle) LIKE @Word OR (' ' + b.NormAuthor) LIKE @Word;

            WITH page AS (
                SELECT b.BookId, b.Title, b.Author,
                       (SELECT COUNT(*) FROM dw.Listing AS l WHERE l.BookId = b.BookId AND l.Status = 0) AS ActiveListings,
                       (SELECT MAX(l.LastSeenAtUtc) FROM dw.Listing AS l WHERE l.BookId = b.BookId) AS LastSeenAtUtc
                FROM dw.Book AS b
                WHERE @Norm IS NULL OR (' ' + b.NormTitle) LIKE @Word OR (' ' + b.NormAuthor) LIKE @Word
                ORDER BY {OrderBy[sort]}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            )
            SELECT p.BookId, p.Title, p.Author, p.ActiveListings, m.MedianPrice, p.LastSeenAtUtc
            FROM page AS p
            OUTER APPLY (
                SELECT DISTINCT CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY l.CurrentPriceRsd) OVER () AS decimal(12,2)) AS MedianPrice
                FROM dw.Listing AS l WHERE l.BookId = p.BookId AND l.Status = 0
            ) AS m
            ORDER BY {OrderBy[sort].Replace("b.", "p.", StringComparison.Ordinal)};
            """;

        await using var connection = new SqlConnection(connectionString.Value);
        var args = new
        {
            Norm = norm,
            Word = norm is null ? null : $"% {Escape(norm)}%",
            Skip = (page - 1) * pageSize,
            Take = pageSize,
        };
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: ct));
        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<BookSummary>()).AsList();
        return (items, total);
    }

    private static string Escape(string value) => value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
