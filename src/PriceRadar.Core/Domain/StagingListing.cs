namespace PriceRadar.Core.Domain;

/// <summary>
/// A listing exactly as scraped, plus the match results written back by MatchJob (ingest.StagingListing).
/// Raw columns are never modified after insert.
/// </summary>
public class StagingListing
{
    public long StagingListingId { get; set; }
    public long ScrapeRunId { get; set; }
    public ScrapeRun? ScrapeRun { get; set; }

    public required string ExternalId { get; set; }
    public required string RawTitle { get; set; }
    public string? RawAuthor { get; set; }
    public string? RawPrice { get; set; }
    public string? RawCondition { get; set; }
    public required string Url { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public DateTime ScrapedAtUtc { get; set; }

    /// <summary>SHA-256 of the raw fields.</summary>
    public required byte[] PayloadHash { get; set; }

    public decimal? PriceRsd { get; set; }
    public string? NormTitle { get; set; }
    public string? NormAuthor { get; set; }
    public int? BookId { get; set; }
    public MatchStatus? MatchStatus { get; set; }
    public decimal? MatchScore { get; set; }

    public ProcessStatus ProcessStatus { get; set; }
    public string? RejectReason { get; set; }
}
