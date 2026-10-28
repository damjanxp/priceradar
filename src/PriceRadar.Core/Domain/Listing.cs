namespace PriceRadar.Core.Domain;

/// <summary>One marketplace ad, latest values overwrite older ones (dw.Listing, type-1 dimension).</summary>
public class Listing
{
    public long ListingId { get; set; }
    public byte SourceId { get; set; }
    public Source? Source { get; set; }
    public required string ExternalId { get; set; }

    /// <summary>Null while the match is Pending.</summary>
    public int? BookId { get; set; }
    public Book? Book { get; set; }
    public MatchStatus MatchStatus { get; set; }
    public decimal? MatchScore { get; set; }

    public required string Title { get; set; }
    public string? Condition { get; set; }
    public required string Url { get; set; }

    /// <summary>Scope in which the listing was first seen.</summary>
    public required string ScopeKey { get; set; }
    public ListingStatus Status { get; set; }
    public decimal CurrentPriceRsd { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public long LastSeenRunId { get; set; }
    public ScrapeRun? LastSeenRun { get; set; }

    /// <summary>Consecutive complete runs of the same scope without this listing.</summary>
    public byte MissedRunCount { get; set; }
    public DateTime? DisappearedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
