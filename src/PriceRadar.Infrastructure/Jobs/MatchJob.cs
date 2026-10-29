using System.Data;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceRadar.Core.Domain;
using PriceRadar.Core.Normalization;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Jobs;

/// <summary>
/// Step 2: parse prices, normalise text and match each staged listing to a canonical book (spec §6). DAY-3 matching is
/// exact <c>NormKey</c> only: a hit is AutoMatched, anything else becomes a NewBook. Fuzzy scoring arrives on DAY-7.
/// </summary>
public sealed class MatchJob(PriceRadarDbContext db, IOptions<PricingSettings> pricing, ILogger<MatchJob> logger)
{
    [Queue(HangfireQueues.Match)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [30, 120])]
    public async Task ExecuteAsync(long scrapeRunId, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = scrapeRunId, ["Job"] = nameof(MatchJob) });

        var run = await db.ScrapeRuns.SingleAsync(r => r.ScrapeRunId == scrapeRunId, ct);
        if (run.Stage >= RunStage.Matched)
        {
            logger.LogInformation("Run {RunId} already matched; nothing to do", scrapeRunId);
            return;
        }

        var rows = await db.StagingListings.AsNoTracking()
            .Where(s => s.ScrapeRunId == scrapeRunId && s.ProcessStatus != ProcessStatus.Loaded)
            .Select(s => new { s.StagingListingId, s.RawTitle, s.RawAuthor, s.RawPrice })
            .ToListAsync(ct);

        var knownAuthors = (await db.Books.AsNoTracking().Where(b => b.NormAuthor != "").Select(b => b.NormAuthor).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var priceOptions = pricing.Value.For(run.EurRsdRate);

        var results = rows.Select(r => Decide(r.StagingListingId, r.RawTitle, r.RawAuthor, r.RawPrice, priceOptions, knownAuthors)).ToList();

        var keys = results.Where(r => r.ProcessStatus == ProcessStatus.Normalized).Select(r => r.NormKey).Distinct().ToList();
        var existing = await db.Books.AsNoTracking().Where(b => keys.Contains(b.NormKey))
            .ToDictionaryAsync(b => b.NormKey, b => b.BookId, StringComparer.Ordinal, ct);

        foreach (var r in results.Where(r => r.ProcessStatus == ProcessStatus.Normalized))
        {
            if (existing.TryGetValue(r.NormKey, out var bookId))
            {
                r.BookId = bookId;
                r.MatchStatus = MatchStatus.AutoMatched;
                r.MatchScore = 1.0m;
            }
            else
            {
                r.MatchStatus = MatchStatus.NewBook;
            }
        }

        await ApplyAsync(scrapeRunId, results, ct);

        run.Stage = RunStage.Matched;
        run.ListingsRejected = results.Count(r => r.ProcessStatus == ProcessStatus.Rejected);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Run {RunId}: {Total} listings → {Auto} auto-matched, {New} new books, {Rejected} rejected",
            scrapeRunId, results.Count, results.Count(r => r.MatchStatus == MatchStatus.AutoMatched),
            results.Count(r => r.MatchStatus == MatchStatus.NewBook), run.ListingsRejected);
    }

    internal static MatchResult Decide(long id, string rawTitle, string? rawAuthor, string? rawPrice, PricingOptions pricing,
        IReadOnlySet<string> knownAuthors)
    {
        var price = PriceParser.Parse(rawPrice, pricing);
        var n = TextNormalizer.Normalize(rawTitle, rawAuthor, knownAuthors);
        var result = new MatchResult(id)
        {
            PriceRsd = price.PriceRsd,
            NormTitle = n.NormTitle,
            NormAuthor = n.NormAuthor,
            NewBookTitle = n.DisplayTitle,
            NewBookAuthor = n.DisplayAuthor,
        };

        if (n.NormTitle.Length == 0)
        {
            result.Reject("EmptyTitle");
        }
        else if (!price.IsValid)
        {
            result.Reject(price.RejectReason!);
        }

        return result;
    }

    private async Task ApplyAsync(long scrapeRunId, List<MatchResult> results, CancellationToken ct)
    {
        if (results.Count == 0)
        {
            return;   // a TVP parameter cannot be an empty record stream
        }

        var tvp = new SqlParameter("@Results", SqlDbType.Structured)
        {
            TypeName = "ingest.MatchResultType",
            Value = MatchResult.ToRecords(results),
        };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC ingest.usp_ApplyMatchResults @ScrapeRunId, @Results",
            [new SqlParameter("@ScrapeRunId", scrapeRunId), tvp], ct);
    }
}

/// <summary>One row of <c>ingest.MatchResultType</c>.</summary>
internal sealed class MatchResult(long stagingListingId)
{
    private static readonly SqlMetaData[] Shape =
    [
        new("StagingListingId", SqlDbType.BigInt),
        new("PriceRsd", SqlDbType.Decimal, 12, 2),
        new("NormTitle", SqlDbType.VarChar, 300),
        new("NormAuthor", SqlDbType.VarChar, 200),
        new("BookId", SqlDbType.Int),
        new("MatchStatus", SqlDbType.TinyInt),
        new("MatchScore", SqlDbType.Decimal, 5, 4),
        new("ProcessStatus", SqlDbType.TinyInt),
        new("RejectReason", SqlDbType.VarChar, 50),
        new("NewBookTitle", SqlDbType.NVarChar, 300),
        new("NewBookAuthor", SqlDbType.NVarChar, 200),
        new("NewBookOrigin", SqlDbType.TinyInt),
        new("NewBookNeedsReview", SqlDbType.Bit),
    ];

    public long StagingListingId { get; } = stagingListingId;
    public decimal? PriceRsd { get; set; }
    public string NormTitle { get; set; } = string.Empty;
    public string NormAuthor { get; set; } = string.Empty;
    public int? BookId { get; set; }
    public MatchStatus? MatchStatus { get; set; }
    public decimal? MatchScore { get; set; }
    public ProcessStatus ProcessStatus { get; set; } = ProcessStatus.Normalized;
    public string? RejectReason { get; set; }
    public string? NewBookTitle { get; set; }
    public string? NewBookAuthor { get; set; }
    public BookOrigin NewBookOrigin { get; set; } = BookOrigin.Rule;
    public bool NewBookNeedsReview { get; set; }

    public string NormKey => $"{NormAuthor}|{NormTitle}";

    public void Reject(string reason)
    {
        ProcessStatus = ProcessStatus.Rejected;
        RejectReason = reason;
        MatchStatus = null;
    }

    public static IEnumerable<SqlDataRecord> ToRecords(IEnumerable<MatchResult> results)
    {
        foreach (var r in results)
        {
            var record = new SqlDataRecord(Shape);
            record.SetInt64(0, r.StagingListingId);
            Set(record, 1, r.PriceRsd);
            record.SetString(2, Truncate(r.NormTitle, 300));
            record.SetString(3, Truncate(r.NormAuthor, 200));
            Set(record, 4, r.BookId);
            Set(record, 5, r.MatchStatus is { } m ? (byte?)m : null);
            Set(record, 6, r.MatchScore);
            record.SetByte(7, (byte)r.ProcessStatus);
            Set(record, 8, r.RejectReason);
            Set(record, 9, r.NewBookTitle is null ? null : Truncate(r.NewBookTitle, 300));
            Set(record, 10, r.NewBookAuthor is null ? null : Truncate(r.NewBookAuthor, 200));
            record.SetByte(11, (byte)r.NewBookOrigin);
            record.SetBoolean(12, r.NewBookNeedsReview);
            yield return record;
        }
    }

    private static void Set(SqlDataRecord record, int ordinal, object? value)
    {
        if (value is null)
        {
            record.SetDBNull(ordinal);
        }
        else
        {
            record.SetValue(ordinal, value);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
