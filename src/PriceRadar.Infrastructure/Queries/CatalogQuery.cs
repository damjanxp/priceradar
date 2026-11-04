using Dapper;
using Microsoft.Data.SqlClient;
using PriceRadar.Core.Normalization;
using PriceRadar.Infrastructure.Staging;

namespace PriceRadar.Infrastructure.Queries;

/// <summary>One catalogue row: a canonical book with what is on sale now and its sold proxy.</summary>
public sealed record CatalogItem(
    int BookId, string Title, string? Author, int ActiveListings, int DisappearedListings,
    decimal? MinPrice, decimal? MedianPrice, decimal? MaxPrice, DateTime? LastSeenAtUtc, string? Sources);

public sealed record CatalogTotals(int Books, int BooksInStock, int ActiveListings, int DisappearedListings);

public enum CatalogSort
{
    Title,
    ActiveDesc,
    MedianAsc,
    MedianDesc,
    LastSeenDesc,
}

/// <summary>
/// The catalogue view of the warehouse (GET /api/catalog): every book with the asking prices of its listings on sale
/// now (min / median / max), the number of copies on sale and the disappeared-listing sold proxy. Prices are the
/// current listing prices, not the 30-day snapshot window the verdict uses.
/// </summary>
public sealed class CatalogQuery(StagingConnectionString connectionString)
{
    // Allow-listed ORDER BY fragments: user input never reaches the SQL text (spec §10.3). Books without a price sort last.
    private static readonly Dictionary<CatalogSort, string> OrderBy = new()
    {
        [CatalogSort.Title] = "b.Title, b.BookId",
        [CatalogSort.ActiveDesc] = "ISNULL(a.ActiveListings, 0) DESC, b.Title, b.BookId",
        [CatalogSort.MedianAsc] = "CASE WHEN m.MedianPrice IS NULL THEN 1 ELSE 0 END, m.MedianPrice, b.Title, b.BookId",
        [CatalogSort.MedianDesc] = "CASE WHEN m.MedianPrice IS NULL THEN 1 ELSE 0 END, m.MedianPrice DESC, b.Title, b.BookId",
        [CatalogSort.LastSeenDesc] = "CASE WHEN a.LastSeenAtUtc IS NULL THEN 1 ELSE 0 END, a.LastSeenAtUtc DESC, b.BookId",
    };

    public async Task<(IReadOnlyList<CatalogItem> Items, int TotalCount, CatalogTotals Totals)> GetPageAsync(
        string? q, bool inStockOnly, int page, int pageSize, CatalogSort sort, CancellationToken ct = default)
    {
        var norm = string.IsNullOrWhiteSpace(q) ? null : TextNormalizer.NormalizeTitle(q);
        var sql = $"""
            WITH a AS (
                SELECT l.BookId,
                       SUM(CASE WHEN l.Status = 0 THEN 1 ELSE 0 END) AS ActiveListings,
                       SUM(CASE WHEN l.Status = 1 THEN 1 ELSE 0 END) AS DisappearedListings,
                       MIN(CASE WHEN l.Status = 0 THEN l.CurrentPriceRsd END) AS MinPrice,
                       MAX(CASE WHEN l.Status = 0 THEN l.CurrentPriceRsd END) AS MaxPrice,
                       MAX(l.LastSeenAtUtc) AS LastSeenAtUtc
                FROM dw.Listing AS l
                GROUP BY l.BookId
            ),
            m AS (
                SELECT DISTINCT l.BookId,
                       CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY l.CurrentPriceRsd) OVER (PARTITION BY l.BookId) AS decimal(12,2)) AS MedianPrice
                FROM dw.Listing AS l
                WHERE l.Status = 0
            ),
            filtered AS (
                SELECT b.BookId, b.Title, b.Author,
                       ISNULL(a.ActiveListings, 0) AS ActiveListings, ISNULL(a.DisappearedListings, 0) AS DisappearedListings,
                       a.MinPrice, m.MedianPrice, a.MaxPrice, a.LastSeenAtUtc,
                       COUNT(*) OVER () AS TotalCount,
                       ROW_NUMBER() OVER (ORDER BY {OrderBy[sort]}) AS RowNo
                FROM dw.Book AS b
                LEFT JOIN a ON a.BookId = b.BookId
                LEFT JOIN m ON m.BookId = b.BookId
                WHERE (@Norm IS NULL OR (' ' + b.NormTitle) LIKE @Word OR (' ' + b.NormAuthor) LIKE @Word)
                  AND (@InStock = 0 OR a.ActiveListings > 0)
            )
            SELECT f.BookId, f.Title, f.Author, f.ActiveListings, f.DisappearedListings, f.MinPrice, f.MedianPrice, f.MaxPrice,
                   f.LastSeenAtUtc, f.TotalCount,
                   (SELECT STRING_AGG(s.Code, ',') WITHIN GROUP (ORDER BY s.Code)
                    FROM ingest.Source AS s
                    WHERE EXISTS (SELECT 1 FROM dw.Listing AS l WHERE l.BookId = f.BookId AND l.SourceId = s.SourceId AND l.Status = 0)) AS Sources
            FROM filtered AS f
            WHERE f.RowNo > @Skip AND f.RowNo <= @Skip + @Take
            ORDER BY f.RowNo;

            SELECT (SELECT COUNT(*) FROM dw.Book) AS Books,
                   (SELECT COUNT(DISTINCT BookId) FROM dw.Listing WHERE Status = 0) AS BooksInStock,
                   (SELECT COUNT(*) FROM dw.Listing WHERE Status = 0) AS ActiveListings,
                   (SELECT COUNT(*) FROM dw.Listing WHERE Status = 1) AS DisappearedListings;
            """;

        await using var connection = new SqlConnection(connectionString.Value);
        var args = new
        {
            Norm = norm,
            Word = norm is null ? null : $"% {Escape(norm)}%",
            InStock = inStockOnly,
            Skip = (page - 1) * pageSize,
            Take = pageSize,
        };
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: ct));
        var rows = (await grid.ReadAsync<CatalogRow>()).AsList();
        var totals = await grid.ReadSingleAsync<CatalogTotals>();
        var total = rows.Count > 0 ? rows[0].TotalCount : await CountAsync(connection, args, ct);
        var items = rows.ConvertAll(r => new CatalogItem(r.BookId, r.Title, r.Author, r.ActiveListings, r.DisappearedListings,
            r.MinPrice, r.MedianPrice, r.MaxPrice, r.LastSeenAtUtc, r.Sources));
        return (items, total, totals);
    }

    // A page past the end has no rows to carry COUNT(*) OVER (), so the total is counted separately.
    private static Task<int> CountAsync(SqlConnection connection, object args, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dw.Book AS b
            WHERE (@Norm IS NULL OR (' ' + b.NormTitle) LIKE @Word OR (' ' + b.NormAuthor) LIKE @Word)
              AND (@InStock = 0 OR EXISTS (SELECT 1 FROM dw.Listing AS l WHERE l.BookId = b.BookId AND l.Status = 0));
            """, args, cancellationToken: ct));

    private static string Escape(string value) => value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private sealed record CatalogRow(
        int BookId, string Title, string? Author, int ActiveListings, int DisappearedListings,
        decimal? MinPrice, decimal? MedianPrice, decimal? MaxPrice, DateTime? LastSeenAtUtc, int TotalCount, string? Sources);
}
