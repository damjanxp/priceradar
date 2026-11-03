namespace PriceRadar.Core.Matching;

/// <summary>A canonical book as the matcher sees it (normalised forms plus display text for prompts).</summary>
public sealed record BookCandidate(int BookId, string NormTitle, string NormAuthor, string Title, string? Author)
{
    public string NormKey => $"{NormAuthor}|{NormTitle}";
}

/// <summary>
/// In-memory inverted index from title token to books (spec §6.2, D-13). Candidates share at least one indexed title
/// token and are ranked by the summed IDF weight of the shared tokens, so rare words count more than common ones.
/// Built once per MatchJob (and kept by the lookup endpoint); 20k books take well under a second.
/// </summary>
public sealed class CandidateIndex
{
    /// <summary>Tokens found in more than this share of all titles carry no information and are not indexed.</summary>
    public const double MaxDocumentFrequency = 0.10;

    private const int MinTokenLength = 3;

    private readonly Dictionary<string, BookCandidate> _byKey;
    private readonly Dictionary<string, int[]> _postings;
    private readonly Dictionary<string, double> _idf;
    private readonly Dictionary<int, BookCandidate> _byId;
    private readonly Dictionary<string, BookCandidate[]> _byAuthor;

    public CandidateIndex(IEnumerable<BookCandidate> books)
    {
        var all = books.ToList();
        _byId = all.ToDictionary(b => b.BookId);
        _byKey = new Dictionary<string, BookCandidate>(StringComparer.Ordinal);
        foreach (var b in all)
        {
            _byKey.TryAdd(b.NormKey, b);
        }

        var postings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var b in all)
        {
            foreach (var token in Tokens(b.NormTitle))
            {
                if (!postings.TryGetValue(token, out var list))
                {
                    postings[token] = list = [];
                }

                list.Add(b.BookId);
            }
        }

        var maxDf = Math.Max(1, (int)(all.Count * MaxDocumentFrequency));
        _postings = postings.Where(p => all.Count < 50 || p.Value.Count <= maxDf)
            .ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        _idf = _postings.ToDictionary(p => p.Key, p => Math.Log((all.Count + 1.0) / p.Value.Length), StringComparer.Ordinal);
        _byAuthor = all.Where(b => b.NormAuthor.Length > 0).GroupBy(b => b.NormAuthor, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        Count = all.Count;
    }

    public int Count { get; }

    public bool TryGetExact(string normTitle, string normAuthor, out BookCandidate book) =>
        _byKey.TryGetValue($"{normAuthor}|{normTitle}", out book!);

    public BookCandidate? Get(int bookId) => _byId.GetValueOrDefault(bookId);

    /// <summary>
    /// Up to <paramref name="max"/> books sharing an indexed title token, best IDF overlap first. Books by the same
    /// author are always added (after those), so a one-word title with a typo ("Gospoiđca") still meets its book.
    /// </summary>
    public IReadOnlyList<BookCandidate> Find(string normTitle, string normAuthor, int max = 20)
    {
        var byTitle = Find(normTitle, max);
        if (normAuthor.Length == 0 || !_byAuthor.TryGetValue(normAuthor, out var sameAuthor))
        {
            return byTitle;
        }

        var seen = byTitle.Select(b => b.BookId).ToHashSet();
        return byTitle.Concat(sameAuthor.Where(b => seen.Add(b.BookId)).Take(max)).ToList();
    }

    /// <summary>Up to <paramref name="max"/> books sharing an indexed title token, best IDF overlap first.</summary>
    public IReadOnlyList<BookCandidate> Find(string normTitle, int max = 20)
    {
        var weights = new Dictionary<int, double>();
        foreach (var token in Tokens(normTitle))
        {
            if (!_postings.TryGetValue(token, out var books))
            {
                continue;
            }

            var idf = _idf[token];
            foreach (var id in books)
            {
                weights[id] = weights.GetValueOrDefault(id) + idf;
            }
        }

        return weights.OrderByDescending(w => w.Value).ThenBy(w => w.Key).Take(max).Select(w => _byId[w.Key]).ToList();
    }

    private static IEnumerable<string> Tokens(string normTitle) =>
        normTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= MinTokenLength).Distinct();
}
