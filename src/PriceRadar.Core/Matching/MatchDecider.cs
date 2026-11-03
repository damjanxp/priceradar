namespace PriceRadar.Core.Matching;

/// <summary>Thresholds from spec §6.4 (configuration section <c>Matching</c>), tuned on the golden set.</summary>
public sealed record MatchOptions(
    double AutoMatchThreshold = 0.90,
    double NewBookThreshold = 0.65,
    double MinMargin = 0.05,
    int MaxCandidates = 20);

public enum MatchOutcome
{
    AutoMatch,
    NewBook,
    Ambiguous,
}

public sealed record ScoredCandidate(BookCandidate Book, double Score);

public sealed record CandidateDecision(MatchOutcome Outcome, ScoredCandidate? Best, IReadOnlyList<ScoredCandidate> Candidates)
{
    public int? BookId => Outcome == MatchOutcome.AutoMatch ? Best!.Book.BookId : null;
}

/// <summary>
/// Exact key → auto; best score ≥ auto threshold with a clear margin → auto; best score below the new-book threshold or
/// no candidates → new book; anything in between → ambiguous (cache, then Claude).
/// </summary>
public sealed class MatchDecider(CandidateIndex index, MatchOptions options)
{
    /// <summary>How many scored candidates an ambiguous decision carries (the Claude prompt shows these).</summary>
    public const int AmbiguousCandidates = 5;

    public MatchOptions Options => options;

    public CandidateDecision Decide(string normTitle, string normAuthor)
    {
        if (normTitle.Length == 0)
        {
            return new CandidateDecision(MatchOutcome.NewBook, null, []);
        }

        if (index.TryGetExact(normTitle, normAuthor, out var exact))
        {
            var hit = new ScoredCandidate(exact, 1.0);
            return new CandidateDecision(MatchOutcome.AutoMatch, hit, [hit]);
        }

        var scored = index.Find(normTitle, normAuthor, options.MaxCandidates)
            .Select(b => new ScoredCandidate(b, SimilarityScorer.Score(normTitle, normAuthor, b.NormTitle, b.NormAuthor)))
            .OrderByDescending(c => c.Score).ThenBy(c => c.Book.BookId)
            .ToList();

        if (scored.Count == 0 || scored[0].Score < options.NewBookThreshold)
        {
            return new CandidateDecision(MatchOutcome.NewBook, scored.FirstOrDefault(), []);
        }

        var best = scored[0];
        var margin = best.Score - (scored.Count > 1 ? scored[1].Score : 0.0);
        if (best.Score >= options.AutoMatchThreshold && margin >= options.MinMargin)
        {
            return new CandidateDecision(MatchOutcome.AutoMatch, best, [best]);
        }

        var shown = scored.Where(c => c.Score >= options.NewBookThreshold).Take(AmbiguousCandidates).ToList();
        return new CandidateDecision(MatchOutcome.Ambiguous, best, shown);
    }
}
