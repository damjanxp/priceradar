using System.ComponentModel.DataAnnotations;
using PriceRadar.Core.Matching;

namespace PriceRadar.Infrastructure.Options;

/// <summary><c>Ai</c> section (spec §6.5–6.6, §10.2). The API key comes only from user-secrets or the environment.</summary>
public sealed class AiSettings
{
    public const string Section = "Ai";

    public bool Enabled { get; set; } = true;

    /// <summary>Never in appsettings or git: <c>dotnet user-secrets set "Ai:ApiKey" …</c> or <c>Ai__ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = "claude-haiku-5-5";

    [RegularExpression("^(low|medium|high|xhigh|max)$")]
    public string Effort { get; set; } = "low";

    [Range(256, 8192)]
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Bump on any prompt change: it is part of the cache key, so old answers are not reused.</summary>
    [Required]
    public string PromptVersion { get; set; } = "v1";

    [Range(0, 10_000)]
    public int MaxCallsPerRun { get; set; } = 200;

    [Range(0, 100)]
    public decimal DailyBudgetUsd { get; set; } = 0.50m;

    public decimal PricePerMTokIn { get; set; } = 0.10m;
    public decimal PricePerMTokOut { get; set; } = 0.50m;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    [Required]
    public string BaseUrl { get; set; } = "https://api.anthropic.com/";

    /// <summary>On only when switched on <i>and</i> a key is configured, so a fresh clone never tries to call the API.</summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(ApiKey);

    public decimal CostUsd(long inputTokens, long outputTokens) =>
        (inputTokens * PricePerMTokIn / 1_000_000m) + (outputTokens * PricePerMTokOut / 1_000_000m);
}

/// <summary><c>Matching</c> section (spec §6.4); defaults are the thresholds chosen on the golden set.</summary>
public sealed class MatchingSettings
{
    public const string Section = "Matching";

    [Range(0.5, 1.0)]
    public double AutoMatchThreshold { get; set; } = 0.90;

    [Range(0.0, 1.0)]
    public double NewBookThreshold { get; set; } = 0.65;

    [Range(0.0, 0.5)]
    public double MinMargin { get; set; } = 0.05;

    [Range(1, 100)]
    public int MaxCandidates { get; set; } = 20;

    public MatchOptions ToOptions() => new(AutoMatchThreshold, NewBookThreshold, MinMargin, MaxCandidates);
}
