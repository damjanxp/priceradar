using Microsoft.Data.SqlClient;

namespace PriceRadar.Infrastructure.Bulk;

/// <summary>Thin wrapper around SqlBulkCopy with the options this project relies on.</summary>
public static class BulkLoader
{
    /// <summary>
    /// Streams <paramref name="rows"/> into <paramref name="table"/>. Columns are mapped by name, never by ordinal.
    /// <c>CheckConstraints</c> keeps foreign keys trusted, so the optimiser can still rely on them after a bulk load.
    /// </summary>
    public static async Task<long> WriteAsync(
        SqlConnection connection,
        string table,
        IReadOnlyList<BulkColumn> columns,
        IEnumerable<object?[]> rows,
        bool keepIdentity,
        int batchSize,
        CancellationToken ct,
        SqlTransaction? transaction = null)
    {
        var options = SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.CheckConstraints;
        if (keepIdentity)
        {
            options |= SqlBulkCopyOptions.KeepIdentity;
        }

        using var bulk = new SqlBulkCopy(connection, options, transaction)
        {
            DestinationTableName = table,
            BatchSize = batchSize,
            BulkCopyTimeout = 0,
            EnableStreaming = true,
        };

        foreach (var column in columns)
        {
            bulk.ColumnMappings.Add(column.Name, column.Name);
        }

        await using var reader = new RowDataReader(columns, rows);
        await bulk.WriteToServerAsync(reader, ct);
        return reader.RowsRead;
    }
}
