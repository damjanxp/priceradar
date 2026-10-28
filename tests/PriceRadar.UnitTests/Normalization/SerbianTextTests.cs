using PriceRadar.Core.Normalization;

namespace PriceRadar.UnitTests.Normalization;

public class SerbianTextTests
{
    [Theory]
    [InlineData("На Дрини ћуприја", "na drini cuprija")]
    [InlineData("Na Drini ćuprija", "na drini cuprija")]
    [InlineData("NA DRINI ĆUPRIJA", "na drini cuprija")]
    [InlineData("na drini cuprija", "na drini cuprija")]
    [InlineData("  Na  Drini\tćuprija ", "na drini cuprija")]
    public void FoldToAscii_SameTitleInEveryScript_GivesOneForm(string input, string expected)
    {
        SerbianText.FoldToAscii(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Đorđe", "djordje")]
    [InlineData("Ђорђе", "djordje")]
    [InlineData("Џиновић", "dzinovic")]
    [InlineData("Džinović", "dzinovic")]
    [InlineData("Љубав и Њиве", "ljubav i njive")]
    [InlineData("Žena šuma čaša", "zena suma casa")]
    public void FoldToAscii_SerbianLetters_FoldPerSpecRule(string input, string expected)
    {
        SerbianText.FoldToAscii(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Prokleta avlija (1954.)", "prokleta avlija 1954")]
    [InlineData("Bašta, pepeo", "basta pepeo")]
    [InlineData("Ivo Andrić - Na Drini ćuprija!!!", "ivo andric na drini cuprija")]
    [InlineData("Seobe 2", "seobe 2")]
    [InlineData("Gabriel García Márquez", "gabriel garcia marquez")]
    public void FoldToAscii_PunctuationAndForeignDiacritics(string input, string expected)
    {
        SerbianText.FoldToAscii(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ... ")]
    public void FoldToAscii_NothingUseful_ReturnsEmpty(string? input)
    {
        SerbianText.FoldToAscii(input).ShouldBe(string.Empty);
    }

    [Fact]
    public void FoldToAscii_OutputIsAlwaysAscii()
    {
        var folded = SerbianText.FoldToAscii("Ћирилица, ćirilica — Ü ö ß € ✓ 日本");

        folded.ShouldAllBe(c => c < 128);
    }
}
