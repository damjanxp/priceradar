using System.ComponentModel.DataAnnotations;
using PriceRadar.Core.Normalization;

namespace PriceRadar.Infrastructure.Options;

/// <summary><c>Pricing</c> section (spec §10.2).</summary>
public sealed class PricingSettings
{
    public const string Section = "Pricing";

    [Range(1, 1000)]
    public decimal EurRsdRate { get; set; } = 117.2m;

    [Range(1, 1_000_000)]
    public decimal MinPriceRsd { get; set; } = 50m;

    [Range(1, 10_000_000)]
    public decimal MaxPriceRsd { get; set; } = 200_000m;

    public PricingOptions For(decimal runEurRsdRate) => new(runEurRsdRate, MinPriceRsd, MaxPriceRsd);
}

/// <summary><c>Sold</c> section: the disappeared-listing heuristic (spec §7).</summary>
public sealed class SoldSettings
{
    public const string Section = "Sold";

    [Range(1, 10)]
    public byte MissedRunsThreshold { get; set; } = 2;

    [Range(1, 365)]
    public int WindowDays { get; set; } = 30;
}
