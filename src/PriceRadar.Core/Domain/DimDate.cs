namespace PriceRadar.Core.Domain;

/// <summary>Date dimension, 2024-01-01 to 2030-12-31 (dw.DimDate). Populated by a migration script.</summary>
public class DimDate
{
    /// <summary>yyyymmdd, e.g. 20261028.</summary>
    public int DateKey { get; set; }
    public DateOnly Date { get; set; }
    public short Year { get; set; }
    public byte Month { get; set; }
    public byte Day { get; set; }
    public byte IsoWeek { get; set; }

    /// <summary>1 = Monday … 7 = Sunday.</summary>
    public byte DayOfWeek { get; set; }
    public bool IsWeekend { get; set; }

    public static int ToKey(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;
}
