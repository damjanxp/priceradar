namespace PriceRadar.Infrastructure.Persistence;

/// <summary>SQL schema names (spec §5.1).</summary>
public static class Schemas
{
    public const string Ingest = "ingest";
    public const string Warehouse = "dw";
    public const string Match = "match";
}
