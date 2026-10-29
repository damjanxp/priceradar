using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceRadar.Core.Normalization;

/// <summary>Normalised forms used for matching plus cleaned display forms for creating new books.</summary>
public sealed record NormalizedListing(string NormTitle, string NormAuthor, string DisplayTitle, string? DisplayAuthor);

/// <summary>
/// The normaliser from spec §6.1 (N1–N8). Applied identically to listings, canonical books and lookup queries, so
/// two spellings of one book end up with the same <c>NormAuthor|NormTitle</c> key.
/// </summary>
public static partial class TextNormalizer
{
    // N6: folded (ASCII) noise phrases, longest first. Single words are only noise after the first word, so titles
    // that start with them ("Roman o Londonu", "Knjiga za Marka", "Nova Gorica") keep their meaning.
    private static readonly string[] NoisePhrases =
    [
        "odlicno ocuvana", "odlicno ocuvano", "odlicno ocuvan", "dobro ocuvana", "dobro ocuvano", "kao nova", "kao novo",
        "kao nov", "tvrdi povez", "tvrd povez", "meki povez", "mek povez", "nekorisceno", "neotpakovano",
    ];

    private static readonly HashSet<string> NoiseWords =
    [
        "nova", "novo", "nov", "ocuvana", "ocuvano", "ocuvan", "izdanje", "izd", "knjiga", "roman", "prodajem", "hitno",
        "akcija", "povoljno", "gratis", "lot", "polovno", "polovna",
    ];

    /// <summary>N1–N7 for a title: fold to ASCII, drop noise phrases and 4-digit years, keep other numbers.</summary>
    public static string NormalizeTitle(string? title)
    {
        var folded = SerbianText.FoldToAscii(title);
        if (folded.Length == 0)
        {
            return folded;
        }

        var text = $" {folded} ";
        foreach (var phrase in NoisePhrases)
        {
            text = text.Replace($" {phrase} ", " ", StringComparison.Ordinal);
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        tokens = tokens
            .Where((t, i) => !IsYear(t) && !(i > 0 && NoiseWords.Contains(t)) && !IsEditionNumber(tokens, i))
            .ToList();

        // Never normalise a title away completely ("Roman", "Nova"): fall back to the plain folded text.
        return tokens.Count > 0 ? string.Join(' ', tokens) : folded;
    }

    /// <summary>N8 author key: folded tokens sorted, so "Ivo Andrić", "Andrić Ivo" and "ИВО АНДРИЋ" agree.</summary>
    public static string NormalizeAuthor(string? author)
    {
        var folded = SerbianText.FoldToAscii(author);
        if (folded.Length == 0)
        {
            return string.Empty;
        }

        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal);
        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Full listing normalisation. When the author field is empty, tries to split "Author - Title" or "Title - Author"
    /// (N8): a side counts as the author if its key is in <paramref name="knownAuthorKeys"/>, otherwise if it alone
    /// looks like a personal name (2–4 capitalised words, no digits).
    /// </summary>
    public static NormalizedListing Normalize(string rawTitle, string? rawAuthor, IReadOnlySet<string>? knownAuthorKeys = null)
    {
        var title = rawTitle.Trim();
        var author = string.IsNullOrWhiteSpace(rawAuthor) ? null : rawAuthor.Trim();

        if (author is null && TrySplitAuthor(title, knownAuthorKeys, out var splitTitle, out var splitAuthor))
        {
            (title, author) = (splitTitle, splitAuthor);
        }

        return new NormalizedListing(
            NormalizeTitle(title),
            NormalizeAuthor(author),
            DisplayTitle(title),
            author is null ? null : Collapse(SerbianText.ToLatin(author)));
    }

    /// <summary>A readable title for a new canonical book: Latin script, trailing noise and years removed.</summary>
    public static string DisplayTitle(string rawTitle)
    {
        var latin = Collapse(SerbianText.ToLatin(rawTitle));
        var segments = Separators().Split(latin)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        // Keep the first segment, plus later ones that still carry meaning after normalisation ("Bašta, pepeo").
        var kept = segments.Take(1)
            .Concat(segments.Skip(1).Where(s => NormalizeTitle(s) != string.Empty && !IsNoiseOnly(s)))
            .ToList();
        var result = kept.Count > 0 ? string.Join(", ", kept) : latin;

        return IsAllUpper(result) ? SentenceCase(result) : result;
    }

    private static bool TrySplitAuthor(string title, IReadOnlySet<string>? known, out string newTitle, out string author)
    {
        newTitle = title;
        author = string.Empty;

        var parts = AuthorDash().Split(title, 2);
        if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
        {
            return false;
        }

        var (left, right) = (parts[0].Trim(), parts[1].Trim());
        var leftKnown = known?.Contains(NormalizeAuthor(left)) == true;
        var rightKnown = known?.Contains(NormalizeAuthor(right)) == true;
        var leftIsAuthor = leftKnown != rightKnown ? leftKnown : LooksLikeName(left) && !LooksLikeName(right);
        var rightIsAuthor = leftKnown != rightKnown ? rightKnown : LooksLikeName(right) && !LooksLikeName(left);

        if (leftIsAuthor == rightIsAuthor)
        {
            return false;   // ambiguous: keep the whole string as the title
        }

        (author, newTitle) = leftIsAuthor ? (left, right) : (right, left);
        return true;
    }

    // "Ivo Andrić", "Danilo Kiš", "Antoan de Sent Egziperi" — but not "Na Drini ćuprija" or "HAZARSKI REČNIK".
    private static bool LooksLikeName(string text)
    {
        var words = SerbianText.ToLatin(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 4 || words.Any(w => w.Any(char.IsDigit)))
        {
            return false;
        }

        var capitalised = words.Count(w => char.IsUpper(w[0]) && w.Skip(1).Any(char.IsLower) && !w.Skip(1).Any(char.IsUpper));
        var particles = words.Count(w => w is "de" or "di" or "van" or "von" or "fon" or "la");
        return capitalised >= 2 && capitalised + particles == words.Length;
    }

    private static bool IsNoiseOnly(string segment)
    {
        var folded = SerbianText.FoldToAscii(segment);
        var text = $" {folded} ";
        foreach (var phrase in NoisePhrases)
        {
            text = text.Replace($" {phrase} ", " ", StringComparison.Ordinal);
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => NoiseWords.Contains(t) || IsYear(t));
    }

    private static bool IsYear(string token) =>
        token.Length == 4 && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1900 and <= 2099;

    // "2 izd" (from "2. izd." or "2. izdanje"): the number belongs to the edition marker, not to the title.
    private static bool IsEditionNumber(List<string> tokens, int i) =>
        i + 1 < tokens.Count && tokens[i].All(char.IsAsciiDigit) && tokens[i + 1] is "izd" or "izdanje";

    private static bool IsAllUpper(string text) => text.Any(char.IsLetter) && !text.Any(char.IsLower);

    private static string SentenceCase(string text)
    {
        var lower = text.ToLower(CultureInfo.InvariantCulture);
        return char.ToUpper(lower[0], CultureInfo.InvariantCulture) + lower[1..];
    }

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // Segment separators for display titles: dashes, brackets, commas, exclamation marks.
    [GeneratedRegex(@"\s[-–—]\s|[()\[\],!]")]
    private static partial Regex Separators();

    // "Author - Title": a spaced dash. Hyphenated names such as "Velmar-Janković" are not split.
    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex AuthorDash();
}
