namespace PriceRadar.Core.Domain;

/// <summary>One distinct question asked of the Claude API and its answer (match.MatchCache, §6.6).</summary>
public class MatchCacheEntry
{
    public long MatchCacheId { get; set; }

    /// <summary>SHA-256 of <c>PromptVersion|NormTitle|NormAuthor</c>.</summary>
    public required byte[] InputHash { get; set; }
    public required string PromptVersion { get; set; }
    public required string NormTitle { get; set; }
    public required string NormAuthor { get; set; }

    /// <summary>Comma-separated candidate BookIds that were shown to the model.</summary>
    public required string CandidateBookIds { get; set; }

    public MatchDecision Decision { get; set; }
    public int? ResolvedBookId { get; set; }
    public decimal? Confidence { get; set; }
    public string? Reason { get; set; }

    public required string Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastHitAtUtc { get; set; }
    public int HitCount { get; set; }
}
