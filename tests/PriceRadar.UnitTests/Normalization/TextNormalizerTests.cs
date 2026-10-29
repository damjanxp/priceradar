using PriceRadar.Core.Normalization;

namespace PriceRadar.UnitTests.Normalization;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("НА ДРИНИ ЋУПРИЈА", "na drini cuprija")]
    [InlineData("Na Drini ćuprija", "na drini cuprija")]
    [InlineData("na drini cuprija, kao nova!", "na drini cuprija")]
    [InlineData("Na Drini ćuprija (tvrd povez)", "na drini cuprija")]
    [InlineData("Na Drini ćuprija - odlično očuvana", "na drini cuprija")]
    [InlineData("Na Drini ćuprija NOVO", "na drini cuprija")]
    [InlineData("Orlovi rano lete, 1977.", "orlovi rano lete")]
    [InlineData("Prokleta avlija (1954.)", "prokleta avlija")]
    [InlineData("Derviš i smrt, 3. izdanje", "dervis i smrt")]
    [InlineData("Tvrđava 2. izd. meki povez", "tvrdjava")]
    public void NormalizeTitle_NoiseAndYears_AreRemoved(string raw, string expected)
    {
        TextNormalizer.NormalizeTitle(raw).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Seobe 2 (1962.)", "seobe 2")]
    [InlineData("Seobe", "seobe")]
    [InlineData("Roman o Londonu", "roman o londonu")]
    [InlineData("Knjiga za Marka", "knjiga za marka")]
    [InlineData("Roman", "roman")]
    [InlineData("1984", "1984")]
    public void NormalizeTitle_MeaningfulWords_AreKept(string raw, string expected)
    {
        TextNormalizer.NormalizeTitle(raw).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Ivo Andrić", "andric ivo")]
    [InlineData("Andrić Ivo", "andric ivo")]
    [InlineData("ИВО АНДРИЋ", "andric ivo")]
    [InlineData("Ivo Andric", "andric ivo")]
    [InlineData("I. Andrić", "andric i")]
    [InlineData("Svetlana Velmar-Janković", "janković svetlana velmar")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizeAuthor_TokensAreSortedAndFolded(string? raw, string expected)
    {
        TextNormalizer.NormalizeAuthor(raw).ShouldBe(expected.Replace("ć", "c"));
    }

    [Theory]
    [InlineData("Ivo Andrić - Na Drini ćuprija", "na drini cuprija", "andric ivo", "Ivo Andrić")]
    [InlineData("Na Drini ćuprija - Ivo Andrić", "na drini cuprija", "andric ivo", "Ivo Andrić")]
    [InlineData("Danilo Kiš - Bašta, pepeo", "basta pepeo", "danilo kis", "Danilo Kiš")]
    [InlineData("Иво Андрић - На Дрини ћуприја", "na drini cuprija", "andric ivo", "Ivo Andrić")]
    public void Normalize_AuthorInTitle_IsSplitOut(string rawTitle, string normTitle, string normAuthor, string displayAuthor)
    {
        var n = TextNormalizer.Normalize(rawTitle, null);

        n.NormTitle.ShouldBe(normTitle);
        n.NormAuthor.ShouldBe(normAuthor);
        n.DisplayAuthor.ShouldBe(displayAuthor);
    }

    [Theory]
    [InlineData("HAZARSKI REČNIK - MILORAD PAVIĆ")]   // both sides all caps: cannot tell which is the name
    [InlineData("Seobe - meki povez")]
    [InlineData("Lovac na zmajeve")]
    public void Normalize_NoClearAuthor_KeepsWholeTitle(string rawTitle)
    {
        var n = TextNormalizer.Normalize(rawTitle, null);

        n.NormAuthor.ShouldBe(string.Empty);
        n.DisplayAuthor.ShouldBeNull();
    }

    [Fact]
    public void Normalize_KnownAuthorKey_ResolvesAllCapsSplit()
    {
        var known = new HashSet<string> { "milorad pavic" };

        var n = TextNormalizer.Normalize("HAZARSKI REČNIK - MILORAD PAVIĆ", null, known);

        n.NormTitle.ShouldBe("hazarski recnik");
        n.NormAuthor.ShouldBe("milorad pavic");
    }

    [Fact]
    public void Normalize_AuthorFieldWins_OverTitleDash()
    {
        var n = TextNormalizer.Normalize("Seobe - meki povez", "Miloš Crnjanski");

        n.NormTitle.ShouldBe("seobe");
        n.NormAuthor.ShouldBe("crnjanski milos");
    }

    [Theory]
    [InlineData("Na Drini ćuprija - kao nova", "Na Drini ćuprija")]
    [InlineData("HAZARSKI REČNIK", "Hazarski rečnik")]
    [InlineData("На Дрини ћуприја", "Na Drini ćuprija")]
    [InlineData("Orlovi rano lete, 1977.", "Orlovi rano lete")]
    [InlineData("Bašta, pepeo", "Bašta, pepeo")]
    [InlineData("Enciklopedija mrtvih (tvrd povez)", "Enciklopedija mrtvih")]
    public void DisplayTitle_IsReadableLatin(string raw, string expected)
    {
        TextNormalizer.DisplayTitle(raw).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_SameBookInFiveSpellings_GivesOneKey()
    {
        string[] keys =
        [
            Key("Na Drini ćuprija", "Ivo Andrić"),
            Key("НА ДРИНИ ЋУПРИЈА", "Иво Андрић"),
            Key("na drini cuprija, kao nova", "Andric Ivo"),
            Key("Ivo Andrić - Na Drini ćuprija (tvrd povez)", null),
            Key("Na Drini ćuprija, 1977.", "ANDRIĆ IVO"),
        ];

        keys.Distinct().ShouldHaveSingleItem().ShouldBe("andric ivo|na drini cuprija");

        static string Key(string title, string? author)
        {
            var n = TextNormalizer.Normalize(title, author);
            return $"{n.NormAuthor}|{n.NormTitle}";
        }
    }
}
