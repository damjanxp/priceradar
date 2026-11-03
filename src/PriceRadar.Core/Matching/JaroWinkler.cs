namespace PriceRadar.Core.Matching;

/// <summary>
/// Jaro-Winkler similarity (0..1): forgiving of small typos, especially towards the end of short strings, which suits
/// surnames ("Andrc" vs "Andric").
/// </summary>
public static class JaroWinkler
{
    private const double PrefixScale = 0.1;
    private const int MaxPrefix = 4;

    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return a.Length == b.Length ? 1.0 : 0.0;
        }

        if (a == b)
        {
            return 1.0;
        }

        var window = Math.Max(0, (Math.Max(a.Length, b.Length) / 2) - 1);
        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var from = Math.Max(0, i - window);
            var to = Math.Min(b.Length - 1, i + window);
            for (var j = from; j <= to; j++)
            {
                if (!bMatched[j] && a[i] == b[j])
                {
                    aMatched[i] = bMatched[j] = true;
                    matches++;
                    break;
                }
            }
        }

        if (matches == 0)
        {
            return 0.0;
        }

        var transpositions = 0;
        for (int i = 0, k = 0; i < a.Length; i++)
        {
            if (!aMatched[i])
            {
                continue;
            }

            while (!bMatched[k])
            {
                k++;
            }

            if (a[i] != b[k])
            {
                transpositions++;
            }

            k++;
        }

        double m = matches;
        var jaro = ((m / a.Length) + (m / b.Length) + ((m - (transpositions / 2.0)) / m)) / 3.0;

        var prefix = 0;
        while (prefix < Math.Min(MaxPrefix, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix])
        {
            prefix++;
        }

        return jaro + (prefix * PrefixScale * (1.0 - jaro));
    }
}
