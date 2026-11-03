using PriceRadar.Core.Matching;
using PriceRadar.Core.Normalization;

namespace PriceRadar.UnitTests.Matching;

public class JaroWinklerTests
{
    [Theory]
    [InlineData("andric", "andric", 1.0)]
    [InlineData("martha", "marhta", 0.961)]   // textbook example
    [InlineData("dwayne", "duane", 0.84)]
    [InlineData("abc", "xyz", 0.0)]
    public void Similarity_KnownValues(string a, string b, double expected)
    {
        JaroWinkler.Similarity(a, b).ShouldBe(expected, 0.005);
    }

    [Fact]
    public void Similarity_IsSymmetric()
    {
        JaroWinkler.Similarity("crnjanski", "crnjansky").ShouldBe(JaroWinkler.Similarity("crnjansky", "crnjanski"));
    }
}

public class SimilarityScorerTests
{
    private static string T(string s) => TextNormalizer.NormalizeTitle(s);
    private static string A(string s) => TextNormalizer.NormalizeAuthor(s);

    [Fact]
    public void Score_SwappedAuthorOrder_IsPerfect()
    {
        SimilarityScorer.AuthorSimilarity(A("Andric Ivo"), A("Ivo Andric")).ShouldBe(1.0);
    }

    [Fact]
    public void AuthorSimilarity_InitialMatchesGivenName()
    {
        SimilarityScorer.AuthorSimilarity(A("I. Andrić"), A("Ivo Andrić")).ShouldBe(1.0);
    }

    [Fact]
    public void Score_TitleTypo_StaysAboveAutoThreshold()
    {
        SimilarityScorer.Score(T("Na Drni cuprija"), A("Ivo Andric"), T("Na Drini ćuprija"), A("Ivo Andrić")).ShouldBeGreaterThan(0.90);
    }

    [Fact]
    public void Score_DifferentVolume_IsFarBelowAutoThreshold()
    {
        SimilarityScorer.Score(T("Seobe 2"), A("Miloš Crnjanski"), T("Seobe"), A("Miloš Crnjanski")).ShouldBeLessThan(0.80);
    }

    [Fact]
    public void Score_SameTitleDifferentAuthor_IsNotAnAutoMatch()
    {
        SimilarityScorer.Score(T("Pesme"), A("Desanka Maksimović"), T("Pesme"), A("Branko Radičević")).ShouldBeLessThan(0.90);
    }

    [Fact]
    public void Score_MissingAuthor_IsCapped()
    {
        SimilarityScorer.Score(T("Na Drini ćuprija"), "", T("Na Drini ćuprija"), A("Ivo Andrić")).ShouldBe(SimilarityScorer.NoAuthorCap);
    }
}

public class MatchDeciderTests
{
    private static BookCandidate Book(int id, string title, string? author) =>
        new(id, TextNormalizer.NormalizeTitle(title), TextNormalizer.NormalizeAuthor(author), title, author);

    private static readonly CandidateIndex Index = new([
        Book(1, "Na Drini ćuprija", "Ivo Andrić"),
        Book(2, "Prokleta avlija", "Ivo Andrić"),
        Book(3, "Seobe", "Miloš Crnjanski"),
        Book(4, "Seobe 2", "Miloš Crnjanski"),
        Book(5, "Pesme", "Desanka Maksimović"),
        Book(6, "Pesme", "Branko Radičević"),
    ]);

    private static CandidateDecision Decide(string title, string? author)
    {
        var n = TextNormalizer.Normalize(title, author);
        return new MatchDecider(Index, new MatchOptions()).Decide(n.NormTitle, n.NormAuthor);
    }

    [Fact]
    public void Decide_ExactKey_IsAutoMatch()
    {
        Decide("НА ДРИНИ ЋУПРИЈА", "Иво Андрић").ShouldSatisfyAllConditions(
            d => d.Outcome.ShouldBe(MatchOutcome.AutoMatch),
            d => d.BookId.ShouldBe(1));
    }

    [Fact]
    public void Decide_Typo_IsAutoMatchWithScore()
    {
        var d = Decide("Prokleta avljia", "Ivo Andric");

        d.Outcome.ShouldBe(MatchOutcome.AutoMatch);
        d.BookId.ShouldBe(2);
        d.Best!.Score.ShouldBeLessThan(1.0);
    }

    [Fact]
    public void Decide_NoAuthor_IsAmbiguousNotAuto()
    {
        Decide("Prokleta avlija", null).Outcome.ShouldBe(MatchOutcome.Ambiguous);
    }

    [Fact]
    public void Decide_UnknownBook_IsNewBook()
    {
        Decide("Hazarski rečnik", "Milorad Pavić").Outcome.ShouldBe(MatchOutcome.NewBook);
    }

    [Fact]
    public void Decide_TwoCloseCandidates_RespectsMargin()
    {
        // "Pesme" by "B. Maksimović": author similar to both? It must never be auto-matched to the wrong poet.
        var d = Decide("Pesme", "Desanka Maksimovic");

        d.Outcome.ShouldBe(MatchOutcome.AutoMatch);
        d.BookId.ShouldBe(5);
    }

    [Fact]
    public void Find_RanksRareTokensFirst()
    {
        Index.Find(TextNormalizer.NormalizeTitle("avlija"), 3).Select(b => b.BookId).ShouldBe([2]);
    }
}
