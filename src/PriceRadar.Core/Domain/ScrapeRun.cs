namespace PriceRadar.Core.Domain;

/// <summary>One manually triggered ingestion run; also the pipeline's state machine (ingest.ScrapeRun).</summary>
public class ScrapeRun
{
    public long ScrapeRunId { get; set; }
    public byte SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>Canonical scope such as <c>kupindo:category:knjige</c>; the sold heuristic compares runs per scope.</summary>
    public required string ScopeKey { get; set; }
    public string? Query { get; set; }

    /// <summary>Fixture sources only: which recorded file the run replays, e.g. <c>run-001</c>.</summary>
    public string? FixtureSet { get; set; }

    public RunStatus Status { get; set; }
    public RunStage Stage { get; set; }

    /// <summary>True when every page in scope was fetched (no page cap hit, no page error).</summary>
    public bool IsComplete { get; set; }
    public short MaxPages { get; set; }
    public short PagesFetched { get; set; }

    public int ListingsFetched { get; set; }
    public int ListingsRejected { get; set; }
    public int ListingsLoaded { get; set; }
    public int AiCalls { get; set; }

    /// <summary>EUR to RSD rate frozen for the whole run.</summary>
    public decimal EurRsdRate { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string? RequestedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }
    public string? HangfireJobId { get; set; }
}
