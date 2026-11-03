using PriceRadar.Core.Pricing;

namespace PriceRadar.UnitTests.Pricing;

public class VerdictPolicyTests
{
    private static PriceDistribution Stats(int n, decimal p25 = 550m, decimal median = 700m, decimal p75 = 900m) =>
        new(n, p25, median, p75, WindowDays: 30);

    [Theory]
    [InlineData(400, Verdict.Cheap)]
    [InlineData(550, Verdict.Cheap)]        // at P25 counts as cheap
    [InlineData(551, Verdict.Fair)]
    [InlineData(700, Verdict.Fair)]
    [InlineData(899, Verdict.Fair)]
    [InlineData(900, Verdict.Expensive)]    // at P75 counts as expensive
    [InlineData(2000, Verdict.Expensive)]
    public void Decide_QuartileBands(int asking, Verdict expected)
    {
        VerdictPolicy.Decide(asking, Stats(21)).Verdict.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Decide_TooFewListings_IsInsufficientData(int n)
    {
        var result = VerdictPolicy.Decide(400, Stats(n));

        result.Verdict.ShouldBe(Verdict.InsufficientData);
        result.Confidence.ShouldBeNull();
    }

    [Theory]
    [InlineData(3, Confidence.Low)]
    [InlineData(5, Confidence.Low)]
    [InlineData(6, Confidence.Medium)]
    [InlineData(14, Confidence.Medium)]
    [InlineData(15, Confidence.High)]
    public void Decide_ConfidenceFollowsSampleSize(int n, Confidence expected)
    {
        VerdictPolicy.Decide(700, Stats(n)).Confidence.ShouldBe(expected);
    }

    [Fact]
    public void Decide_Explanation_NamesTheFigures()
    {
        var result = VerdictPolicy.Decide(400, Stats(21));

        result.Explanation.ShouldBe("400 RSD is at or below the 25th percentile (550 RSD) of 21 listings seen in the last 30 days.");
    }

    [Fact]
    public void Decide_NullPercentiles_IsInsufficientData()
    {
        VerdictPolicy.Decide(400, new PriceDistribution(5, null, null, null, 30)).Verdict.ShouldBe(Verdict.InsufficientData);
    }
}
