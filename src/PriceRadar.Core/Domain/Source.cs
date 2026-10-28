namespace PriceRadar.Core.Domain;

/// <summary>A marketplace or data feed that listings come from (ingest.Source).</summary>
public class Source
{
    public byte SourceId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public SourceKind Kind { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsEnabled { get; set; }
    public int MinDelayMs { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
