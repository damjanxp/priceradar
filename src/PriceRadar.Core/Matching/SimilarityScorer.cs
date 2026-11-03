using FuzzySharp;

namespace PriceRadar.Core.Matching;

/// <summary>Similarity between a listing and a canonical book on normalised text (spec §6.3, D-14).</summary>
public static class SimilarityScorer
{
    /// <summary>A listing or book without an author can never score above this, so it is never auto-matched.</summary>
    public const double NoAuthorCap = 0.85;

    /// <summary>
    /// <c>0.7·TitleSim + 0.3·AuthorSim</c> when both sides have an author; otherwise <c>min(0.85·TitleSim, 0.85)</c>.
    /// Inputs are normalised forms (ASCII, author tokens sorted).
    /// </summary>
    public static double Score(string listingTitle, string listingAuthor, string bookTitle, string bookAuthor)
    {
        var title = TitleSimilarity(listingTitle, bookTitle);
        if (listingAuthor.Length == 0 || bookAuthor.Length == 0)
        {
            return Math.Min(NoAuthorCap * title, NoAuthorCap);
        }

        return (0.7 * title) + (0.3 * AuthorSimilarity(listingAuthor, bookAuthor));
    }

    /// <summary>
    /// Token-set ratio ignores word order and extra words; token-sort ratio still penalises them a little, so
    /// "seobe" vs "seobe 2" scores below an exact title (volumes are different books).
    /// </summary>
    public static double TitleSimilarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        if (a == b)
        {
            return 1.0;
        }

        var set = Fuzz.TokenSetRatio(a, b) / 100.0;
        var sort = Fuzz.TokenSortRatio(a, b) / 100.0;
        var score = (0.6 * set) + (0.4 * sort);

        // Different numbers (volumes, parts) mean different books, however similar the rest is.
        return DigitsDiffer(a, b) ? Math.Min(score, 0.6) : score;
    }

    /// <summary>
    /// Author keys are sorted token lists ("andric ivo"), so the surname position is unknown. Each token of the shorter
    /// name is aligned with its best partner in the longer one: equal tokens or an initial ("i" ↔ "ivo") score 1,
    /// anything else its Jaro-Winkler similarity. The result is the average over the shorter name.
    /// </summary>
    public static double AuthorSimilarity(string a, string b)
    {
        if (a == b)
        {
            return 1.0;
        }

        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var (shorter, longer) = ta.Length <= tb.Length ? (ta, tb) : (tb, ta);
        if (shorter.Length == 0)
        {
            return 0.0;
        }

        var used = new bool[longer.Length];
        var total = 0.0;
        foreach (var token in shorter)
        {
            var best = 0.0;
            var bestIndex = -1;
            for (var i = 0; i < longer.Length; i++)
            {
                if (used[i])
                {
                    continue;
                }

                var s = TokenSimilarity(token, longer[i]);
                if (s > best)
                {
                    (best, bestIndex) = (s, i);
                }
            }

            if (bestIndex >= 0)
            {
                used[bestIndex] = true;
            }

            total += best;
        }

        return total / shorter.Length;
    }

    private static double TokenSimilarity(string x, string y)
    {
        if (x == y)
        {
            return 1.0;
        }

        // An initial matches a full given name that starts with it.
        if ((x.Length == 1 && y.StartsWith(x, StringComparison.Ordinal)) || (y.Length == 1 && x.StartsWith(y, StringComparison.Ordinal)))
        {
            return 1.0;
        }

        return x.Length == 1 || y.Length == 1 ? 0.0 : JaroWinkler.Similarity(x, y);
    }

    private static bool DigitsDiffer(string a, string b)
    {
        static string Numbers(string s) => string.Join(' ', s.Split(' ').Where(t => t.All(char.IsAsciiDigit)).Order(StringComparer.Ordinal));
        return Numbers(a) != Numbers(b);
    }
}
