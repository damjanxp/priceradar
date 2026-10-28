namespace PriceRadar.Core.Domain;

/// <summary>
/// Fact table row. Grain: one observed price of one listing in one successful run (dw.PriceSnapshot, D-08).
/// BookId and SourceId are copied from the listing so stats queries need no join (D-09).
/// </summary>
public class PriceSnapshot
{
    public long PriceSnapshotId { get; set; }
    public long ListingId { get; set; }
    public Listing? Listing { get; set; }
    public int? BookId { get; set; }
    public byte SourceId { get; set; }
    public long ScrapeRunId { get; set; }
    public int DateKey { get; set; }
    public decimal PriceRsd { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}
