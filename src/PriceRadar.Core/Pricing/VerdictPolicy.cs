using System.Globalization;

namespace PriceRadar.Core.Pricing;

public enum Verdict
{
    Cheap,
    Fair,
    Expensive,
    InsufficientData,
    Unknown,
}

public enum Confidence
{
    Low,
    Medium,
    High,
}

/// <summary>The figures a verdict is based on (subset of dw.usp_GetBookStats).</summary>
public sealed record PriceDistribution(int SampleSize, decimal? P25, decimal? Median, decimal? P75, int WindowDays);

public sealed record VerdictResult(Verdict Verdict, Confidence? Confidence, string Explanation);

/// <summary>
/// Quartile verdict (spec §8.4, D-22): at or below P25 is cheap, at or above P75 expensive, anything between fair.
/// Fewer than three listings in the window is not enough to judge.
/// </summary>
public static class VerdictPolicy
{
    public const int MinimumSample = 3;

    public static VerdictResult Decide(decimal askingPrice, PriceDistribution stats)
    {
        var n = stats.SampleSize;
        if (n < MinimumSample || stats.P25 is not { } p25 || stats.P75 is not { } p75)
        {
            return new VerdictResult(Verdict.InsufficientData, null,
                $"Only {n} listing{(n == 1 ? "" : "s")} seen in the last {stats.WindowDays} days; at least {MinimumSample} are needed for a verdict.");
        }

        var confidence = n switch
        {
            < 6 => Confidence.Low,
            < 15 => Confidence.Medium,
            _ => Confidence.High,
        };
        var sample = $"{n} listings seen in the last {stats.WindowDays} days";

        if (askingPrice <= p25)
        {
            return new VerdictResult(Verdict.Cheap, confidence,
                $"{Rsd(askingPrice)} is at or below the 25th percentile ({Rsd(p25)}) of {sample}.");
        }

        if (askingPrice >= p75)
        {
            return new VerdictResult(Verdict.Expensive, confidence,
                $"{Rsd(askingPrice)} is at or above the 75th percentile ({Rsd(p75)}) of {sample}.");
        }

        return new VerdictResult(Verdict.Fair, confidence,
            $"{Rsd(askingPrice)} is between the 25th ({Rsd(p25)}) and 75th ({Rsd(p75)}) percentiles of {sample}" +
            (stats.Median is { } m ? $"; the median is {Rsd(m)}." : "."));
    }

    private static string Rsd(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture) + " RSD";
}
