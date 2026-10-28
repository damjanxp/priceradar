namespace PriceRadar.Core.Abstractions;

/// <summary>
/// Every way of acquiring listings sits behind this interface (spec §4.5), so the pipeline cannot tell
/// fixture, synthetic and live data apart.
/// </summary>
public interface IListingSource
{
    /// <summary>Matches <c>ingest.Source.Code</c>, e.g. <c>fixture</c>, <c>kp</c>, <c>kupindo</c>.</summary>
    string Code { get; }

    /// <summary>
    /// Streams the listings in scope. Implementations update <see cref="ScrapeRequest.Progress"/> so the run
    /// can record pages fetched and whether the scope was covered completely (needed by the sold heuristic).
    /// </summary>
    IAsyncEnumerable<RawListing> FetchAsync(ScrapeRequest request, CancellationToken ct);
}

/// <summary>A listing as the source delivered it. Nothing is parsed or normalised yet.</summary>
public sealed record RawListing(
    string ExternalId,
    string Title,
    string? Author,
    string? Price,
    string? Condition,
    string Url,
    DateTime? PostedAtUtc);

public sealed class ScrapeRequest
{
    public required string SourceCode { get; init; }
    public required string ScopeKey { get; init; }
    public string? Query { get; init; }
    public short MaxPages { get; init; } = 10;

    /// <summary>Fixture sources only: which file to replay, e.g. <c>run-001</c>.</summary>
    public string? FixtureSet { get; init; }

    public ScrapeProgress Progress { get; } = new();
}

public sealed class ScrapeProgress
{
    public short PagesFetched { get; set; }

    /// <summary>True only when every page in scope was fetched without hitting the page cap or an error.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Capture time to stamp on the run; fixture files carry their own so replays are deterministic.</summary>
    public DateTime? CapturedAtUtc { get; set; }
}
