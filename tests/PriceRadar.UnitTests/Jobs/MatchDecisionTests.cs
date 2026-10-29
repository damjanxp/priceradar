using PriceRadar.Core.Domain;
using PriceRadar.Core.Normalization;
using PriceRadar.Infrastructure.Jobs;

namespace PriceRadar.UnitTests.Jobs;

public class MatchDecisionTests
{
    private static readonly PricingOptions Pricing = new();
    private static readonly HashSet<string> NoAuthors = [];

    [Fact]
    public void Decide_ValidListing_IsNormalisedWithDisplayForms()
    {
        var r = MatchJob.Decide(1, "НА ДРИНИ ЋУПРИЈА - kao nova", "Иво Андрић", "1.200 din", Pricing, NoAuthors);

        r.ProcessStatus.ShouldBe(ProcessStatus.Normalized);
        r.PriceRsd.ShouldBe(1200m);
        r.NormKey.ShouldBe("andric ivo|na drini cuprija");
        // All-caps ads get sentence case; proper nouns inside the title cannot be recovered ("Drini" → "drini").
        r.NewBookTitle.ShouldBe("Na drini ćuprija");
        r.NewBookAuthor.ShouldBe("Ivo Andrić");
    }

    [Theory]
    [InlineData("Dogovor", "NoPrice")]
    [InlineData("1 din", "PriceOutOfRange")]
    [InlineData(null, "NoPrice")]
    public void Decide_UnusablePrice_IsRejected(string? price, string reason)
    {
        var r = MatchJob.Decide(1, "Seobe", "Miloš Crnjanski", price, Pricing, NoAuthors);

        r.ProcessStatus.ShouldBe(ProcessStatus.Rejected);
        r.RejectReason.ShouldBe(reason);
        r.MatchStatus.ShouldBeNull();
    }

    [Fact]
    public void Decide_TitleWithoutLetters_IsRejectedAsEmpty()
    {
        var r = MatchJob.Decide(1, " !!! ", null, "500 din", Pricing, NoAuthors);

        r.RejectReason.ShouldBe("EmptyTitle");
    }

    [Fact]
    public void Decide_EuroPrice_UsesTheRunRate()
    {
        var r = MatchJob.Decide(1, "Seobe", "Miloš Crnjanski", "10 €", new PricingOptions(EurRsdRate: 117.5m), NoAuthors);

        r.PriceRsd.ShouldBe(1175m);
    }
}
