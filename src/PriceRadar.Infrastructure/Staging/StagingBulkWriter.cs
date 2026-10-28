using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Bulk;

namespace PriceRadar.Infrastructure.Staging;

public sealed record StagingWriteResult(int Written, int DuplicatesSkipped);

/// <summary>Lands raw listings in ingest.StagingListing in batches of 1,000 (spec §4.2 step 1, FR-4).</summary>
public sealed class StagingBulkWriter(StagingConnectionString connectionString, ILogger<StagingBulkWriter> logger)
{
    public const int BatchSize = 1_000;

    private static readonly BulkColumn[] Columns =
    [
        new("ScrapeRunId", typeof(long)),
        new("ExternalId", typeof(string)),
        new("RawTitle", typeof(string)),
        new("RawAuthor", typeof(string)),
        new("RawPrice", typeof(string)),
        new("RawCondition", typeof(string)),
        new("Url", typeof(string)),
        new("PostedAtUtc", typeof(DateTime)),
        new("ScrapedAtUtc", typeof(DateTime)),
        new("PayloadHash", typeof(byte[])),
        new("ProcessStatus", typeof(byte)),
    ];

    /// <summary>
    /// Writes every listing of the stream for <paramref name="scrapeRunId"/>. Any rows already staged for the run are
    /// deleted first, so a retried ScrapeJob restarts cleanly (spec §9.2). Duplicate ExternalIds within the stream are
    /// skipped (the first one wins).
    /// </summary>
    public async Task<StagingWriteResult> WriteAsync(
        long scrapeRunId, IAsyncEnumerable<RawListing> listings, Func<DateTime> scrapedAtUtc, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync(ct);

        await using (var delete = new SqlCommand("DELETE FROM ingest.StagingListing WHERE ScrapeRunId = @id", connection))
        {
            delete.Parameters.AddWithValue("@id", scrapeRunId);
            var removed = await delete.ExecuteNonQueryAsync(ct);
            if (removed > 0)
            {
                logger.LogInformation("Removed {Count} staging rows left by an earlier attempt of run {RunId}", removed, scrapeRunId);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var batch = new List<object?[]>(BatchSize);
        var written = 0;
        var duplicates = 0;

        await foreach (var listing in listings.WithCancellation(ct))
        {
            if (!seen.Add(listing.ExternalId))
            {
                duplicates++;
                continue;
            }

            batch.Add(ToRow(scrapeRunId, listing, scrapedAtUtc()));
            if (batch.Count == BatchSize)
            {
                written += await FlushAsync(connection, batch, ct);
            }
        }

        written += await FlushAsync(connection, batch, ct);
        return new StagingWriteResult(written, duplicates);
    }

    private static async Task<int> FlushAsync(SqlConnection connection, List<object?[]> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        var count = (int)await BulkLoader.WriteAsync(
            connection, "ingest.StagingListing", Columns, batch, keepIdentity: false, BatchSize, ct);
        batch.Clear();
        return count;
    }

    private static object?[] ToRow(long scrapeRunId, RawListing l, DateTime scrapedAtUtc) =>
    [
        scrapeRunId, l.ExternalId, l.Title, l.Author, l.Price, l.Condition, l.Url, l.PostedAtUtc, scrapedAtUtc,
        PayloadHash(l), (byte)ProcessStatus.New,
    ];

    /// <summary>SHA-256 over the raw fields, separated by a unit-separator character so field boundaries count.</summary>
    public static byte[] PayloadHash(RawListing l)
    {
        var payload = string.Join('\u001f', l.ExternalId, l.Title, l.Author, l.Price, l.Condition, l.Url,
            l.PostedAtUtc?.ToString("O"));
        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }
}

/// <summary>The SQL connection string, wrapped so it can be injected without passing IConfiguration around.</summary>
public sealed record StagingConnectionString(string Value);
