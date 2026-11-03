using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PriceRadar.Core.Normalization;
using PriceRadar.Core.Pricing;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Queries;

public enum LookupMatchStatus
{
    Matched,
    Ambiguous,
    NoMatch,
}

public sealed record LookupCandidate(int BookId, string Title, string? Author, int ActiveListings);

public sealed record LookupMatch(LookupMatchStatus Status, string? Method, int? BookId, string? Title, string? Author, decimal? Score,
    IReadOnlyList<LookupCandidate> Candidates);

public sealed record LookupQueryEcho(string Title, string? Author, decimal AskingPrice, string NormTitle, string NormAuthor);

public sealed record LookupResult(LookupQueryEcho Query, LookupMatch Match, Verdict? Verdict, Confidence? Confidence, BookStats? Stats,
    string Explanation, long ElapsedMs);

/// <summary>
/// GET /api/lookup (FR-15, spec §8.4): normalise the query, find the canonical book, compare the asking price with its
/// statistics. Deterministic only — the lookup never calls the Claude API (D-19). DAY-5 matching is exact NormKey, then
/// title-only; the candidate index with fuzzy scores replaces the fallback on DAY-7.
/// </summary>
public sealed class LookupService(PriceRadarDbContext db, BookStatsQuery stats)
{
    private const int MaxCandidates = 3;

    public async Task<LookupResult> LookupAsync(string title, string? author, decimal askingPrice, int windowDays, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var normTitle = TextNormalizer.NormalizeTitle(title);
        var normAuthor = TextNormalizer.NormalizeAuthor(author);
        var echo = new LookupQueryEcho(title, author, askingPrice, normTitle, normAuthor);

        var match = await MatchAsync(normTitle, normAuthor, ct);
        if (match.Status != LookupMatchStatus.Matched)
        {
            var explanation = match.Status == LookupMatchStatus.Ambiguous
                ? $"{match.Candidates.Count} books match this title; pick one to see its price statistics."
                : "No book in the catalogue matches this title and author.";
            return new LookupResult(echo, match, match.Status == LookupMatchStatus.NoMatch ? Verdict.Unknown : null, null, null,
                explanation, sw.ElapsedMilliseconds);
        }

        var bookStats = await stats.GetStatsAsync(match.BookId!.Value, windowDays, ct: ct);
        var verdict = VerdictPolicy.Decide(askingPrice,
            new PriceDistribution(bookStats.SampleSize, bookStats.P25, bookStats.Median, bookStats.P75, windowDays));
        return new LookupResult(echo, match, verdict.Verdict, verdict.Confidence, bookStats, verdict.Explanation, sw.ElapsedMilliseconds);
    }

    private async Task<LookupMatch> MatchAsync(string normTitle, string normAuthor, CancellationToken ct)
    {
        if (normTitle.Length == 0)
        {
            return new LookupMatch(LookupMatchStatus.NoMatch, null, null, null, null, null, []);
        }

        var key = $"{normAuthor}|{normTitle}";
        var exact = await db.Books.AsNoTracking().Where(b => b.NormKey == key)
            .Select(b => new { b.BookId, b.Title, b.Author }).SingleOrDefaultAsync(ct);
        if (exact is not null)
        {
            return new LookupMatch(LookupMatchStatus.Matched, "rule", exact.BookId, exact.Title, exact.Author, 1.0m, []);
        }

        // Same title, any author: decisive only when the query has no author and exactly one book has that title.
        // Anonymous projection first: EF cannot ORDER BY a member of a record built through its constructor.
        var sameTitle = (await db.Books.AsNoTracking()
            .Where(b => b.NormTitle == normTitle)
            .Select(b => new
            {
                b.BookId,
                b.Title,
                b.Author,
                ActiveListings = db.Listings.Count(l => l.BookId == b.BookId && l.Status == Core.Domain.ListingStatus.Active),
            })
            .OrderByDescending(c => c.ActiveListings).ThenBy(c => c.BookId)
            .Take(MaxCandidates + 1)
            .ToListAsync(ct))
            .Select(c => new LookupCandidate(c.BookId, c.Title, c.Author, c.ActiveListings))
            .ToList();

        if (normAuthor.Length == 0 && sameTitle.Count == 1)
        {
            var only = sameTitle[0];
            return new LookupMatch(LookupMatchStatus.Matched, "rule-title", only.BookId, only.Title, only.Author, null, []);
        }

        return sameTitle.Count > 0
            ? new LookupMatch(LookupMatchStatus.Ambiguous, null, null, null, null, null, sameTitle.Take(MaxCandidates).ToList())
            : new LookupMatch(LookupMatchStatus.NoMatch, null, null, null, null, null, []);
    }
}
