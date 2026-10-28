using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceRadar.Core.Normalization;

public sealed record PricingOptions(decimal EurRsdRate = 117.2m, decimal MinPriceRsd = 50m, decimal MaxPriceRsd = 200_000m);

public sealed record PriceParseResult(decimal? PriceRsd, string? RejectReason, bool WasEur)
{
    public bool IsValid => PriceRsd is not null;
}

/// <summary>
/// Turns raw marketplace price text into RSD (FR-5). Serbian formatting is assumed: '.' groups thousands,
/// ',' separates decimals ("1.200,00 RSD"). EUR prices are converted with the run's frozen rate.
/// </summary>
public static partial class PriceParser
{
    public const string NoPrice = "NoPrice";
    public const string PriceOutOfRange = "PriceOutOfRange";

    public static PriceParseResult Parse(string? raw, PricingOptions options)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new PriceParseResult(null, NoPrice, false);
        }

        var text = raw.Trim().ToLowerInvariant();
        var isEur = text.Contains('€') || EurWord().IsMatch(text);

        var match = Amount().Match(text);
        if (!match.Success || !TryParseAmount(match.Value, out var amount))
        {
            return new PriceParseResult(null, NoPrice, isEur);   // "Dogovor", "Kontakt", "Pozvati" …
        }

        var rsd = isEur ? Math.Round(amount * options.EurRsdRate, 2, MidpointRounding.AwayFromZero) : amount;
        if (rsd < options.MinPriceRsd || rsd > options.MaxPriceRsd)
        {
            return new PriceParseResult(null, PriceOutOfRange, isEur);
        }

        return new PriceParseResult(rsd, null, isEur);
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var digits = value.Replace(" ", string.Empty).Replace(" ", string.Empty);
        var lastDot = digits.LastIndexOf('.');
        var lastComma = digits.LastIndexOf(',');

        string invariant;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both present: whichever comes last is the decimal separator.
            var decimalSep = lastComma > lastDot ? ',' : '.';
            var groupSep = decimalSep == ',' ? '.' : ',';
            invariant = digits.Replace(groupSep.ToString(), string.Empty).Replace(decimalSep, '.');
        }
        else if (lastComma >= 0)
        {
            invariant = IsThousandsGrouping(digits, ',') ? digits.Replace(",", string.Empty) : digits.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            invariant = IsThousandsGrouping(digits, '.') ? digits.Replace(".", string.Empty) : digits;
        }
        else
        {
            invariant = digits;
        }

        return decimal.TryParse(invariant, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    // "1.200" / "1.200.000" are grouped thousands; "15.5" / "12,50" are decimals.
    private static bool IsThousandsGrouping(string digits, char separator)
    {
        var parts = digits.Split(separator);
        return parts.Length > 1 && parts[0].Length is >= 1 and <= 3 && parts.Skip(1).All(p => p.Length == 3);
    }

    [GeneratedRegex(@"\d[\d\s .,]*\d|\d")]
    private static partial Regex Amount();

    [GeneratedRegex(@"\beur(a|o)?\b|\bevr(a|o)?\b")]
    private static partial Regex EurWord();
}
