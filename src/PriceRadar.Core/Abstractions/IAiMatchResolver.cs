using PriceRadar.Core.Domain;

namespace PriceRadar.Core.Abstractions;

public sealed record AiCandidate(int BookId, string Title, string? Author, double Score);

public sealed record AiMatchRequest(string ListingTitle, string? ListingAuthor, string NormTitle, string NormAuthor,
    IReadOnlyList<AiCandidate> Candidates);

/// <summary>The model's answer, already validated against the candidates (spec §6.5).</summary>
public sealed record AiMatchAnswer(MatchDecision Decision, int? BookId, decimal? Confidence, string? Reason, string Model,
    int InputTokens, int OutputTokens, int LatencyMs);

/// <summary>Resolves one ambiguous match. Implemented by the Claude client; faked in tests and when AI is disabled.</summary>
public interface IAiMatchResolver
{
    Task<AiMatchAnswer> ResolveAsync(AiMatchRequest request, CancellationToken ct);
}

/// <summary>A failure that retrying will not fix (bad request, wrong API key): stop calling for this run.</summary>
public sealed class AiResolverException(string message, int? statusCode = null, Exception? inner = null) : Exception(message, inner)
{
    public int? StatusCode { get; } = statusCode;
}
