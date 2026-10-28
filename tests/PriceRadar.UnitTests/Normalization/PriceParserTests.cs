using PriceRadar.Core.Normalization;

namespace PriceRadar.UnitTests.Normalization;

public class PriceParserTests
{
    private static readonly PricingOptions Options = new(EurRsdRate: 117.2m, MinPriceRsd: 50m, MaxPriceRsd: 200_000m);

    [Theory]
    [InlineData("1.200 din", 1200)]
    [InlineData("1.200,00 RSD", 1200)]
    [InlineData("850 din", 850)]
    [InlineData("850din", 850)]
    [InlineData("Cena: 1.350 dinara", 1350)]
    [InlineData("1 200 din", 1200)]
    [InlineData("1.200.000 din", null)]          // valid number, but above MaxPriceRsd
    [InlineData("650,00 RSD", 650)]
    [InlineData("  420 RSD  ", 420)]
    public void Parse_DinarFormats_ReturnsRsd(string raw, int? expected)
    {
        var result = PriceParser.Parse(raw, Options);

        result.PriceRsd.ShouldBe(expected is null ? null : (decimal)expected);
    }

    [Theory]
    [InlineData("15 €", 1758.00)]
    [InlineData("15,00 €", 1758.00)]
    [InlineData("12,50 €", 1465.00)]
    [InlineData("15.5 EUR", 1816.60)]
    [InlineData("4 eura", 468.80)]
    public void Parse_EuroFormats_ConvertsWithRunRate(string raw, double expected)
    {
        var result = PriceParser.Parse(raw, Options);

        result.WasEur.ShouldBeTrue();
        result.PriceRsd.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("Dogovor")]
    [InlineData("Kontakt")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_NoNumber_RejectsAsNoPrice(string? raw)
    {
        var result = PriceParser.Parse(raw, Options);

        result.IsValid.ShouldBeFalse();
        result.RejectReason.ShouldBe(PriceParser.NoPrice);
    }

    [Theory]
    [InlineData("1 din")]
    [InlineData("49 din")]
    [InlineData("250.000 din")]
    [InlineData("2.000 €")]
    public void Parse_OutsideBounds_RejectsAsOutOfRange(string raw)
    {
        var result = PriceParser.Parse(raw, Options);

        result.IsValid.ShouldBeFalse();
        result.RejectReason.ShouldBe(PriceParser.PriceOutOfRange);
    }

    [Fact]
    public void Parse_BoundaryValues_AreInclusive()
    {
        PriceParser.Parse("50 din", Options).PriceRsd.ShouldBe(50m);
        PriceParser.Parse("200.000 din", Options).PriceRsd.ShouldBe(200_000m);
    }
}
