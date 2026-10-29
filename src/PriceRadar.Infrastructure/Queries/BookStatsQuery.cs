using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PriceRadar.Infrastructure.Staging;

namespace PriceRadar.Infrastructure.Queries;

/// <summary>Result of dw.usp_GetBookStats. Price fields are null when no listing was seen in the window.</summary>
public sealed record BookStats(
    int BookId,
    int WindowDays,
    DateTime AsOfUtc,
    int SampleSize,
    decimal? Min,
    decimal? P25,
    decimal? Median,
    decimal? P75,
    decimal? Max,
    int ActiveListings,
    int DisappearedInWindow);

public sealed record PricePoint(DateTime Period, decimal MinPrice, decimal MedianPrice, decimal MaxPrice, int Snapshots);

/// <summary>Read side for book statistics: Dapper over stored procedures (D-04, D-05).</summary>
public sealed class BookStatsQuery(StagingConnectionString connectionString, TimeProvider clock)
{
    public async Task<BookStats> GetStatsAsync(int bookId, int windowDays = 30, DateTime? asOfUtc = null, CancellationToken ct = default)
    {
        var asOf = asOfUtc ?? clock.GetUtcNow().UtcDateTime;
        await using var connection = new SqlConnection(connectionString.Value);

        var command = new CommandDefinition("dw.usp_GetBookStats",
            new { BookId = bookId, WindowDays = windowDays, AsOfUtc = asOf },
            commandType: CommandType.StoredProcedure, cancellationToken: ct);
        await using var grid = await connection.QueryMultipleAsync(command);

        var prices = await grid.ReadSingleOrDefaultAsync<PriceRow>();
        var supply = await grid.ReadSingleAsync<SupplyRow>();

        return new BookStats(bookId, windowDays, asOf, prices?.SampleSize ?? 0,
            prices?.MinPrice, prices?.P25, prices?.Median, prices?.P75, prices?.MaxPrice,
            supply.ActiveListings, supply.DisappearedInWindow);
    }

    public async Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(int bookId, DateOnly from, DateOnly to, string bucket, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString.Value);
        var command = new CommandDefinition("dw.usp_GetPriceHistory",
            new { BookId = bookId, FromDateKey = ToKey(from), ToDateKey = ToKey(to), Bucket = bucket },
            commandType: CommandType.StoredProcedure, cancellationToken: ct);
        return (await connection.QueryAsync<PricePoint>(command)).AsList();
    }

    private static int ToKey(DateOnly d) => Core.Domain.DimDate.ToKey(d);

    private sealed record PriceRow(int SampleSize, decimal MinPrice, decimal MaxPrice, decimal P25, decimal Median, decimal P75);

    private sealed record SupplyRow(int ActiveListings, int DisappearedInWindow);
}
