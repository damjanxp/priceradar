using System.Globalization;
using System.Text;
using PriceRadar.Core.Matching;
using PriceRadar.Core.Normalization;
using PriceRadar.Infrastructure.Sources;
using Xunit.Abstractions;

namespace PriceRadar.UnitTests.Matching;

/// <summary>
/// Matching quality on the labelled golden set (spec §6.4, DAY-7): tests/fixtures/matching/{catalog,golden}.csv.
/// The threshold grid is printed (run with --logger "console;verbosity=detailed") and recorded in the README.
/// </summary>
[Trait("Category", "Golden")]
public class GoldenSetTests(ITestOutputHelper output)
{
    private static readonly string Folder = Path.GetFullPath(Path.Combine(FixturePaths.Resolve(), "..", "matching"));

    private sealed record Row(string Title, string Author, string Expected, string Kind);

    public sealed record Metrics(int Total, int CorrectAuto, int WrongAuto, int Ambiguous, int FalseNew, int CorrectNew, double Precision, double Recall)
    {
        public double AmbiguousRate => (double)Ambiguous / Total;
    }

    private static (CandidateIndex Index, Dictionary<string, int> IdByLabel, HashSet<string> AuthorKeys) Catalog()
    {
        var books = ReadCsv("catalog.csv")
            .Select((r, i) => new BookCandidate(i + 1, TextNormalizer.NormalizeTitle(r[0]), TextNormalizer.NormalizeAuthor(r[1]), r[0], r[1]))
            .ToList();
        return (new CandidateIndex(books), books.ToDictionary(b => $"{b.Author} | {b.Title}", b => b.BookId),
            books.Select(b => b.NormAuthor).ToHashSet(StringComparer.Ordinal));
    }

    private static List<Row> Golden() => ReadCsv("golden.csv").Select(r => new Row(r[0], r[1], r[2], r[3])).ToList();

    public static Metrics Evaluate(MatchOptions options, List<(string Row, string Outcome)>? mistakes = null)
    {
        var (index, idByLabel, authors) = Catalog();
        var decider = new MatchDecider(index, options);
        int correctAuto = 0, wrongAuto = 0, ambiguous = 0, falseNew = 0, correctNew = 0, existing = 0;
        var rows = Golden();

        foreach (var row in rows)
        {
            var n = TextNormalizer.Normalize(row.Title, string.IsNullOrEmpty(row.Author) ? null : row.Author, authors);
            var d = decider.Decide(n.NormTitle, n.NormAuthor);
            var expectedId = idByLabel.TryGetValue(row.Expected, out var id) ? id : (int?)null;
            existing += expectedId is null ? 0 : 1;

            switch (d.Outcome)
            {
                case MatchOutcome.AutoMatch when d.BookId == expectedId:
                    correctAuto++;
                    break;
                case MatchOutcome.AutoMatch:
                    wrongAuto++;
                    mistakes?.Add(($"{row.Title} / {row.Author} [{row.Kind}]", $"auto-matched {index.Get(d.BookId!.Value)!.Title} / {index.Get(d.BookId!.Value)!.Author}"));
                    break;
                case MatchOutcome.Ambiguous:
                    ambiguous++;
                    break;
                case MatchOutcome.NewBook when row.Expected == "NEW":
                    correctNew++;
                    break;
                case MatchOutcome.NewBook:
                    falseNew++;
                    mistakes?.Add(($"{row.Title} / {row.Author} [{row.Kind}]", "new book (missed duplicate)"));
                    break;
            }
        }

        var precision = correctAuto + wrongAuto == 0 ? 1.0 : (double)correctAuto / (correctAuto + wrongAuto);
        return new Metrics(rows.Count, correctAuto, wrongAuto, ambiguous, falseNew, correctNew, precision, (double)correctAuto / existing);
    }

    [Fact]
    public void DefaultThresholds_MeetPrecisionTarget()
    {
        var mistakes = new List<(string, string)>();
        var m = Evaluate(new MatchOptions(), mistakes);

        output.WriteLine($"Default thresholds (auto 0.90, new 0.65, margin 0.05) on {m.Total} rows:");
        output.WriteLine($"  auto precision {m.Precision:P1}, auto recall {m.Recall:P1}, ambiguous {m.AmbiguousRate:P1} ({m.Ambiguous}), " +
                         $"missed duplicates {m.FalseNew}, wrong auto-matches {m.WrongAuto}, new books found {m.CorrectNew}");
        foreach (var (row, outcome) in mistakes)
        {
            output.WriteLine($"  ✗ {row} → {outcome}");
        }

        m.Precision.ShouldBeGreaterThanOrEqualTo(0.95);
    }

    [Fact]
    public void ThresholdGrid_IsPrinted()
    {
        var sb = new StringBuilder("auto  new   | precision  recall  ambiguous  missed-dup  wrong-auto\n");
        foreach (var auto in new[] { 0.85, 0.88, 0.90, 0.92, 0.95 })
        {
            foreach (var @new in new[] { 0.55, 0.60, 0.65, 0.70 })
            {
                var m = Evaluate(new MatchOptions(auto, @new));
                sb.AppendLine(CultureInfo.InvariantCulture, $"{auto:0.00}  {@new:0.00}  |   {m.Precision,6:P1}  {m.Recall,6:P1}    {m.AmbiguousRate,6:P1}      {m.FalseNew,3}         {m.WrongAuto,3}");
            }
        }

        output.WriteLine(sb.ToString());
    }

    [Fact]
    public void GoldenSet_HasExpectedShape()
    {
        var rows = Golden();
        rows.Count.ShouldBeGreaterThanOrEqualTo(150);
        rows.Count(r => r.Kind.StartsWith("trap:", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(15);
    }

    /// <summary>Minimal RFC 4180 reader (quoted fields may contain commas); returns data rows without the header.</summary>
    private static List<string[]> ReadCsv(string file)
    {
        var rows = new List<string[]>();
        foreach (var line in File.ReadLines(Path.Combine(Folder, file), Encoding.UTF8).Skip(1).Where(l => l.Length > 0))
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        return rows;
    }
}
