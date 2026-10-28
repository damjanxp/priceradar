using System.Globalization;
using System.Text;

namespace PriceRadar.Core.Normalization;

/// <summary>
/// Character-level steps of the normaliser (spec §6.1 N1–N5): Cyrillic → Latin, lower case, diacritics → ASCII,
/// punctuation → space. Noise phrases, years and author handling (N6–N8) live in TextNormalizer (DAY-3).
/// </summary>
public static class SerbianText
{
    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['ђ'] = "đ", ['е'] = "e", ['ж'] = "ž",
        ['з'] = "z", ['и'] = "i", ['ј'] = "j", ['к'] = "k", ['л'] = "l", ['љ'] = "lj", ['м'] = "m", ['н'] = "n",
        ['њ'] = "nj", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['ћ'] = "ć", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "č", ['џ'] = "dž", ['ш'] = "š",
        ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D", ['Ђ'] = "Đ", ['Е'] = "E", ['Ж'] = "Ž",
        ['З'] = "Z", ['И'] = "I", ['Ј'] = "J", ['К'] = "K", ['Л'] = "L", ['Љ'] = "Lj", ['М'] = "M", ['Н'] = "N",
        ['Њ'] = "Nj", ['О'] = "O", ['П'] = "P", ['Р'] = "R", ['С'] = "S", ['Т'] = "T", ['Ћ'] = "Ć", ['У'] = "U",
        ['Ф'] = "F", ['Х'] = "H", ['Ц'] = "C", ['Ч'] = "Č", ['Џ'] = "Dž", ['Ш'] = "Š",
    };

    /// <summary>N2: Serbian Cyrillic to Latin; other characters pass through unchanged.</summary>
    public static string ToLatin(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            if (CyrillicToLatin.TryGetValue(ch, out var latin))
            {
                sb.Append(latin);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// N1–N5: NFC, Cyrillic → Latin, lower case, <c>č,ć→c š→s ž→z đ→dj dž→dz</c>, every other character that is not
    /// an ASCII letter or digit becomes a space, whitespace collapsed. Output is pure ASCII.
    /// </summary>
    public static string FoldToAscii(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var latin = ToLatin(text.Normalize(NormalizationForm.FormC)).ToLower(CultureInfo.InvariantCulture);
        var sb = new StringBuilder(latin.Length + 4);
        var pendingSpace = false;

        foreach (var ch in latin)
        {
            var folded = ch switch
            {
                'č' or 'ć' => "c",
                'š' => "s",
                'ž' => "z",
                'đ' => "dj",
                _ when ch is >= 'a' and <= 'z' or >= '0' and <= '9' => null,
                _ => FoldOther(ch),
            };

            if (folded is null)
            {
                AppendWithSpace(sb, ch.ToString(), ref pendingSpace);
            }
            else if (folded.Length == 0)
            {
                pendingSpace = sb.Length > 0;
            }
            else
            {
                AppendWithSpace(sb, folded, ref pendingSpace);
            }
        }

        return sb.ToString();
    }

    private static void AppendWithSpace(StringBuilder sb, string value, ref bool pendingSpace)
    {
        if (pendingSpace)
        {
            sb.Append(' ');
            pendingSpace = false;
        }

        sb.Append(value);
    }

    // Other Latin letters with diacritics (é, ü, ő …) lose their marks; everything else separates words.
    private static string FoldOther(char ch)
    {
        var decomposed = ch.ToString().Normalize(NormalizationForm.FormD);
        var baseChar = decomposed[0];
        return baseChar is >= 'a' and <= 'z' ? baseChar.ToString() : string.Empty;
    }
}
