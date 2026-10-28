using System.Diagnostics;
using Microsoft.Data.SqlClient;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Bulk;
using PriceRadar.Infrastructure.Staging;

namespace PriceRadar.Seeder;

internal sealed record SeedOptions(int Books, int Listings, int Snapshots, int Days, int Seed, DateOnly EndDate, bool Reset)
{
    public static SeedOptions From(CliArgs cli) => new(
        cli.Int("books", 20_000),
        cli.Int("listings", 60_000),
        cli.Int("snapshots", 500_000),
        cli.Int("days", 180),
        cli.Int("seed", 42),
        cli.Date("end-date") ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
        cli.Flag("reset"));
}

/// <summary>
/// Generates the deterministic synthetic warehouse (spec §11.3): Zipf-distributed book popularity, one synthetic
/// ScrapeRun per day, listings that live for a contiguous range of runs, and one PriceSnapshot per listing per run.
/// Everything is bulk-loaded with SqlBulkCopy.
/// </summary>
internal sealed class SeedCommand(StagingConnectionString connectionString)
{
    private const byte SyntheticSourceId = 2;
    private const string ScopeKey = "synthetic:category:knjige";
    // Zipf–Mandelbrot weights 1 / (rank + q)^s. A plain Zipf (q = 0) puts ~15 % of all snapshots on one book;
    // q = 10 keeps a strong skew (top books have thousands of snapshots, the median book ~a dozen) without that.
    private const double ZipfExponent = 1.1;
    private const double ZipfOffset = 10;
    private const int MaxListingLifetimeRuns = 16;
    private const int BatchSize = 10_000;

    private static readonly string?[] Conditions = ["Odlično", "Vrlo dobro", "Dobro", "Oštećeno", null];
    private static readonly string[] TitleNoise = [" - kao nova", " (tvrd povez)", " odlično očuvana", " meki povez"];

    public async Task<int> RunAsync(SeedOptions o, CancellationToken ct)
    {
        if (o.Snapshots < o.Listings)
        {
            throw new ArgumentException("--snapshots must be at least --listings (every listing is seen once).");
        }

        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync(ct);

        var existing = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM ingest.ScrapeRun WHERE SourceId = 2", ct);
        if (existing > 0 && !o.Reset)
        {
            throw new InvalidOperationException("Synthetic data already exists. Re-run with --reset to replace it.");
        }

        var total = Stopwatch.StartNew();
        if (o.Reset)
        {
            var sw = Stopwatch.StartNew();
            await ResetAsync(connection, ct);
            Console.WriteLine($"Reset: removed previous synthetic data ({sw.Elapsed.TotalSeconds:0.0} s)");
        }

        var data = Generate(o, await ScalarAsync<int>(connection, "SELECT ISNULL(MAX(BookId), 0) FROM dw.Book", ct),
            await ScalarAsync<long>(connection, "SELECT ISNULL(MAX(ListingId), 0) FROM dw.Listing", ct),
            await ScalarAsync<long>(connection, "SELECT ISNULL(MAX(ScrapeRunId), 0) FROM ingest.ScrapeRun", ct));

        var runs = await TimedAsync("Runs", () => BulkLoader.WriteAsync(connection, "ingest.ScrapeRun", RunColumns, data.RunRows(), true, BatchSize, ct));
        var books = await TimedAsync("Books", () => BulkLoader.WriteAsync(connection, "dw.Book", BookColumns, data.BookRows(), true, BatchSize, ct));
        var listings = await TimedAsync("Listings", () => BulkLoader.WriteAsync(connection, "dw.Listing", ListingColumns, data.ListingRows(), true, BatchSize, ct));
        var snapshots = await TimedAsync("Snapshots", () => BulkLoader.WriteAsync(connection, "dw.PriceSnapshot", SnapshotColumns, data.SnapshotRows(), false, BatchSize, ct));

        // KeepIdentity inserts explicit IDs; make sure later EF inserts continue after them.
        await ExecuteAsync(connection, "DBCC CHECKIDENT ('ingest.ScrapeRun', RESEED) WITH NO_INFOMSGS; DBCC CHECKIDENT ('dw.Book', RESEED) WITH NO_INFOMSGS; DBCC CHECKIDENT ('dw.Listing', RESEED) WITH NO_INFOMSGS;", ct);

        var (hot, cold) = data.HotAndColdBooks();
        Console.WriteLine($"Done in {total.Elapsed.TotalSeconds:0.0} s | runs={runs} books={books} listings={listings} snapshots={snapshots}");
        Console.WriteLine($"hot={hot.BookId} ({hot.Snapshots} snapshots)  cold={cold.BookId} ({cold.Snapshots} snapshots, median)");
        Console.WriteLine("Top 5 books by snapshots: " + string.Join(", ", data.TopBooks(5).Select(b => $"{b.BookId}:{b.Snapshots}")));
        return 0;
    }

    private static SyntheticData Generate(SeedOptions o, int bookBase, long listingBase, long runBase)
    {
        var rng = new Random(o.Seed);
        var catalog = SyntheticCatalog.Generate(rng, o.Books);

        // Popularity: a random permutation assigns Zipf ranks, so the hottest book is not simply the first ID.
        var byRank = Enumerable.Range(0, catalog.Count).ToArray();
        rng.Shuffle(byRank);
        var cumulative = new double[catalog.Count];
        var sum = 0d;
        for (var k = 0; k < cumulative.Length; k++)
        {
            sum += 1d / Math.Pow(k + 1 + ZipfOffset, ZipfExponent);
            cumulative[k] = sum;
        }

        var listings = new SyntheticListing[o.Listings];
        var lifetimeTotal = 0L;
        for (var i = 0; i < listings.Length; i++)
        {
            var rank = Array.BinarySearch(cumulative, rng.NextDouble() * sum);
            var book = byRank[rank < 0 ? ~rank : rank];
            var start = rng.Next(o.Days);
            var length = Math.Min(1 + rng.Next(MaxListingLifetimeRuns), o.Days - start);
            var price = Round10(catalog[book].MedianPrice * (decimal)Math.Exp(Gaussian(rng) * 0.25));
            var title = catalog[book].Title + (rng.Next(5) == 0 ? TitleNoise[rng.Next(TitleNoise.Length)] : string.Empty);
            listings[i] = new SyntheticListing(book, start, length, Math.Clamp(price, 50m, 200_000m), title, Conditions[rng.Next(Conditions.Length)]);
            lifetimeTotal += length;
        }

        AdjustToSnapshotTarget(rng, listings, o.Snapshots - lifetimeTotal, o.Days);
        return new SyntheticData(o, rng, catalog, listings, bookBase, listingBase, runBase);
    }

    /// <summary>Lengthens or shortens random listing lifetimes until the snapshot count hits the target exactly.</summary>
    private static void AdjustToSnapshotTarget(Random rng, SyntheticListing[] listings, long diff, int days)
    {
        var guard = 0L;
        while (diff != 0)
        {
            if (++guard > 100_000_000)
            {
                throw new InvalidOperationException("Cannot reach the snapshot target with these parameters.");
            }

            ref var l = ref listings[rng.Next(listings.Length)];
            if (diff > 0 && l.Start + l.Length < days)
            {
                l = l with { Length = l.Length + 1 };
                diff--;
            }
            else if (diff < 0 && l.Length > 1)
            {
                l = l with { Length = l.Length - 1 };
                diff++;
            }
        }
    }

    private static async Task ResetAsync(SqlConnection connection, CancellationToken ct) =>
        await ExecuteAsync(connection, """
            DELETE FROM dw.PriceSnapshot WHERE SourceId = 2;
            DELETE FROM dw.Listing WHERE SourceId = 2;
            DELETE b FROM dw.Book AS b
            WHERE b.Origin = 0
              AND NOT EXISTS (SELECT 1 FROM dw.Listing AS l WHERE l.BookId = b.BookId)
              AND NOT EXISTS (SELECT 1 FROM dw.PriceSnapshot AS p WHERE p.BookId = b.BookId)
              AND NOT EXISTS (SELECT 1 FROM match.MatchCache AS m WHERE m.ResolvedBookId = b.BookId);
            DELETE s FROM ingest.StagingListing AS s
            JOIN ingest.ScrapeRun AS r ON r.ScrapeRunId = s.ScrapeRunId WHERE r.SourceId = 2;
            DELETE FROM ingest.ScrapeRun WHERE SourceId = 2;
            """, ct);

    private static async Task<long> TimedAsync(string label, Func<Task<long>> work)
    {
        var sw = Stopwatch.StartNew();
        var rows = await work();
        Console.WriteLine($"{label,-10} {rows,9:N0} rows  {sw.Elapsed.TotalSeconds,6:0.0} s  ({rows / Math.Max(sw.Elapsed.TotalSeconds, 0.001),9:N0} rows/s)");
        return rows;
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync(ct))!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 600 };
        await command.ExecuteNonQueryAsync(ct);
    }

    private static decimal Round10(decimal value) => Math.Round(value / 10m, MidpointRounding.AwayFromZero) * 10m;

    private static double Gaussian(Random rng) =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2.0 * Math.PI * rng.NextDouble());

    private static readonly BulkColumn[] RunColumns =
    [
        new("ScrapeRunId", typeof(long)), new("SourceId", typeof(byte)), new("ScopeKey", typeof(string)), new("Status", typeof(byte)),
        new("Stage", typeof(byte)), new("IsComplete", typeof(bool)), new("MaxPages", typeof(short)), new("PagesFetched", typeof(short)),
        new("ListingsFetched", typeof(int)), new("ListingsRejected", typeof(int)), new("ListingsLoaded", typeof(int)), new("AiCalls", typeof(int)),
        new("EurRsdRate", typeof(decimal)), new("IdempotencyKey", typeof(Guid)), new("RequestedBy", typeof(string)),
        new("CreatedAtUtc", typeof(DateTime)), new("StartedAtUtc", typeof(DateTime)), new("FinishedAtUtc", typeof(DateTime)),
    ];

    private static readonly BulkColumn[] BookColumns =
    [
        new("BookId", typeof(int)), new("Title", typeof(string)), new("Author", typeof(string)), new("NormTitle", typeof(string)),
        new("NormAuthor", typeof(string)), new("Origin", typeof(byte)), new("NeedsReview", typeof(bool)), new("CreatedAtUtc", typeof(DateTime)),
    ];

    private static readonly BulkColumn[] ListingColumns =
    [
        new("ListingId", typeof(long)), new("SourceId", typeof(byte)), new("ExternalId", typeof(string)), new("BookId", typeof(int)),
        new("MatchStatus", typeof(byte)), new("MatchScore", typeof(decimal)), new("Title", typeof(string)), new("Condition", typeof(string)),
        new("Url", typeof(string)), new("ScopeKey", typeof(string)), new("Status", typeof(byte)), new("CurrentPriceRsd", typeof(decimal)),
        new("FirstSeenAtUtc", typeof(DateTime)), new("LastSeenAtUtc", typeof(DateTime)), new("LastSeenRunId", typeof(long)),
        new("MissedRunCount", typeof(byte)), new("DisappearedAtUtc", typeof(DateTime)),
    ];

    private static readonly BulkColumn[] SnapshotColumns =
    [
        new("ListingId", typeof(long)), new("BookId", typeof(int)), new("SourceId", typeof(byte)), new("ScrapeRunId", typeof(long)),
        new("DateKey", typeof(int)), new("PriceRsd", typeof(decimal)), new("CapturedAtUtc", typeof(DateTime)),
    ];

    private readonly record struct SyntheticListing(int Book, int Start, int Length, decimal StartPrice, string Title, string? Condition)
    {
        public int LastRun => Start + Length - 1;
    }

    /// <summary>The generated dataset plus the row streams for each table.</summary>
    private sealed class SyntheticData
    {
        private readonly SeedOptions _o;
        private readonly List<CatalogBook> _catalog;
        private readonly SyntheticListing[] _listings;
        private readonly int _bookBase;
        private readonly long _listingBase;
        private readonly long _runBase;
        private readonly int[] _snapListing;
        private readonly short[] _snapRun;
        private readonly decimal[] _snapPrice;
        private readonly decimal[] _finalPrice;
        private readonly int[] _perRun;
        private readonly Guid[] _runKeys;

        public SyntheticData(SeedOptions o, Random rng, List<CatalogBook> catalog, SyntheticListing[] listings, int bookBase, long listingBase, long runBase)
        {
            (_o, _catalog, _listings, _bookBase, _listingBase, _runBase) = (o, catalog, listings, bookBase, listingBase, runBase);
            _snapListing = new int[o.Snapshots];
            _snapRun = new short[o.Snapshots];
            _snapPrice = new decimal[o.Snapshots];
            _finalPrice = listings.Select(l => l.StartPrice).ToArray();
            _perRun = new int[o.Days];
            _runKeys = Enumerable.Range(0, o.Days).Select(_ => { var b = new byte[16]; rng.NextBytes(b); return new Guid(b); }).ToArray();

            // Snapshots in chronological order (run by run), like real loads; prices occasionally drop.
            var n = 0;
            for (var r = 0; r < o.Days; r++)
            {
                for (var i = 0; i < listings.Length; i++)
                {
                    if (r < listings[i].Start || r > listings[i].LastRun)
                    {
                        continue;
                    }

                    if (r > listings[i].Start && rng.Next(20) == 0)
                    {
                        _finalPrice[i] = Math.Max(50m, Round10(_finalPrice[i] * 0.9m));
                    }

                    _snapListing[n] = i;
                    _snapRun[n] = (short)r;
                    _snapPrice[n] = _finalPrice[i];
                    _perRun[r]++;
                    n++;
                }
            }
        }

        private DateTime RunTime(int r) => _o.EndDate.AddDays(r - (_o.Days - 1)).ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
        private long RunId(int r) => _runBase + r + 1;
        private int BookId(int book) => _bookBase + book + 1;

        public IEnumerable<object?[]> RunRows() => Enumerable.Range(0, _o.Days).Select(r => new object?[]
        {
            RunId(r), SyntheticSourceId, ScopeKey, (byte)RunStatus.Succeeded, (byte)RunStage.Loaded, true, (short)10, (short)10,
            _perRun[r], 0, _perRun[r], 0, 117.2m, _runKeys[r], "seeder", RunTime(r), RunTime(r), RunTime(r).AddMinutes(2),
        });

        public IEnumerable<object?[]> BookRows() => _catalog.Select((b, i) => new object?[]
        {
            BookId(i), b.Title, b.Author, b.NormTitle, b.NormAuthor, (byte)BookOrigin.Seed, false, RunTime(0),
        });

        public IEnumerable<object?[]> ListingRows()
        {
            var last = _o.Days - 1;
            for (var i = 0; i < _listings.Length; i++)
            {
                var l = _listings[i];
                var missed = last - l.LastRun;
                var disappeared = missed >= 2;   // N = 2 complete runs without the listing (spec §7)
                var externalId = $"syn-{i + 1:D6}";
                yield return
                [
                    _listingBase + i + 1, SyntheticSourceId, externalId, BookId(l.Book), (byte)MatchStatus.AutoMatched, 1.0000m,
                    l.Title, l.Condition, $"https://synthetic.priceradar.local/listing/{externalId}", ScopeKey,
                    (byte)(disappeared ? ListingStatus.Disappeared : ListingStatus.Active), _finalPrice[i],
                    RunTime(l.Start), RunTime(l.LastRun), RunId(l.LastRun), (byte)Math.Min(missed, 2),
                    disappeared ? RunTime(l.LastRun + 2) : null,
                ];
            }
        }

        public IEnumerable<object?[]> SnapshotRows()
        {
            for (var n = 0; n < _snapListing.Length; n++)
            {
                var i = _snapListing[n];
                int r = _snapRun[n];
                yield return
                [
                    _listingBase + i + 1, BookId(_listings[i].Book), SyntheticSourceId, RunId(r),
                    DimDate.ToKey(DateOnly.FromDateTime(RunTime(r))), _snapPrice[n], RunTime(r),
                ];
            }
        }

        private (int BookId, int Snapshots)[] SnapshotsPerBook()
        {
            var counts = new int[_catalog.Count];
            foreach (var i in _snapListing)
            {
                counts[_listings[i].Book]++;
            }

            return counts.Select((c, b) => (BookId(b), c)).Where(x => x.c > 0)
                .OrderByDescending(x => x.c).ThenBy(x => x.Item1).ToArray();
        }

        public IEnumerable<(int BookId, int Snapshots)> TopBooks(int n) => SnapshotsPerBook().Take(n);

        public ((int BookId, int Snapshots) Hot, (int BookId, int Snapshots) Cold) HotAndColdBooks()
        {
            var books = SnapshotsPerBook();
            return (books[0], books[books.Length / 2]);
        }
    }
}
