using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Options;

namespace PriceRadar.Infrastructure.Ai;

/// <summary>
/// Claude Messages API client for ambiguous matches (spec §6.5, D-16, D-17): a typed HttpClient with the standard
/// resilience handler. The answer is forced into a JSON schema (structured outputs) and then checked against the
/// candidates, because a schema-valid answer can still name a book that was never offered.
/// </summary>
public sealed class ClaudeMatchResolver(HttpClient http, IOptions<AiSettings> options, ILogger<ClaudeMatchResolver> logger) : IAiMatchResolver
{
    public const string ApiVersion = "2026-10-21";
    private const decimal MinMatchConfidence = 0.6m;

    /// <summary>Prompt v1 (spec §6.5). Any edit here must bump <c>Ai:PromptVersion</c>.</summary>
    public const string SystemPrompt = """
        You match second-hand book advertisements from Serbian marketplaces to a catalogue.
        Ads may be Cyrillic or Latin, may omit diacritics, may swap author first/last name,
        and may contain condition words ("kao nova", "tvrd povez") that are not part of the title.
        Different translations or editions of the same work by the same author are the SAME book.
        Different volumes ("Seobe 1" vs "Seobe 2") are DIFFERENT books.
        Choose "match" only if one candidate is clearly the same work; choose "new_book" if none is;
        choose "unsure" if you cannot tell. Never invent a bookId that is not in the candidates.
        """;

    private static readonly JsonNode OutputSchema = JsonNode.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["decision", "bookId", "confidence", "reason"],
          "properties": {
            "decision":   { "type": "string", "enum": ["match", "new_book", "unsure"] },
            "bookId":     { "type": ["integer", "null"] },
            "confidence": { "type": "number" },
            "reason":     { "type": "string" }
          }
        }
        """)!;

    public async Task<AiMatchAnswer> ResolveAsync(AiMatchRequest request, CancellationToken ct)
    {
        var settings = options.Value;
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(BuildBody(request, settings).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-api-key", settings.ApiKey);
        message.Headers.Add("anthropic-version", ApiVersion);

        var sw = Stopwatch.StartNew();
        using var response = await http.SendAsync(message, ct);   // 429/5xx are retried by the resilience handler
        var body = await response.Content.ReadAsStringAsync(ct);
        var latency = (int)sw.ElapsedMilliseconds;

        if (!response.IsSuccessStatusCode)
        {
            // The body explains 400s (e.g. a rejected parameter); it never contains the key.
            var detail = body.Length > 500 ? body[..500] : body;
            if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                throw new HttpRequestException($"Claude API {(int)response.StatusCode} after retries: {detail}", null, response.StatusCode);
            }

            throw new AiResolverException($"Claude API rejected the request ({(int)response.StatusCode}): {detail}", (int)response.StatusCode);
        }

        var answer = Parse(body, request, settings.Model, latency);
        logger.LogInformation(
            "Claude {Model} answered {Decision} for {NormTitle} in {LatencyMs} ms ({InputTokens} in / {OutputTokens} out tokens)",
            answer.Model, answer.Decision, request.NormTitle, latency, answer.InputTokens, answer.OutputTokens);
        return answer;
    }

    internal static JsonObject BuildBody(AiMatchRequest request, AiSettings settings)
    {
        var input = new JsonObject
        {
            ["listing"] = new JsonObject
            {
                ["title"] = request.ListingTitle,
                ["author"] = request.ListingAuthor,
                ["normTitle"] = request.NormTitle,
                ["normAuthor"] = request.NormAuthor,
            },
            ["candidates"] = new JsonArray(request.Candidates.Select(c => (JsonNode)new JsonObject
            {
                ["bookId"] = c.BookId,
                ["title"] = c.Title,
                ["author"] = c.Author,
                ["score"] = Math.Round(c.Score, 2),
            }).ToArray()),
        };

        // No temperature / top_p / top_k: API version 2026-10-21 rejects sampling parameters (D-17).
        return new JsonObject
        {
            ["model"] = settings.Model,
            ["max_tokens"] = settings.MaxTokens,
            ["output_config"] = new JsonObject
            {
                ["effort"] = settings.Effort,
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = OutputSchema.DeepClone() },
            },
            ["system"] = SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = input.ToJsonString() }),
        };
    }

    internal static AiMatchAnswer Parse(string body, AiMatchRequest request, string model, int latencyMs)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var usage = root.TryGetProperty("usage", out var u) ? u : default;
        var inTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : 0;
        var outTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
        var usedModel = root.TryGetProperty("model", out var m) ? m.GetString() ?? model : model;

        AiMatchAnswer Answer(MatchDecision decision, int? bookId, decimal? confidence, string? reason) =>
            new(decision, bookId, confidence, reason is { Length: > 500 } ? reason[..500] : reason, usedModel, inTokens, outTokens, latencyMs);

        var stop = root.TryGetProperty("stop_reason", out var s) ? s.GetString() : null;
        switch (stop)
        {
            case "refusal":
                return Answer(MatchDecision.Unsure, null, null, "The model declined to answer (stop_reason: refusal).");
            case "max_tokens":
                return Answer(MatchDecision.Error, null, null, "Output was cut off (stop_reason: max_tokens).");
            case not "end_turn":
                return Answer(MatchDecision.Error, null, null, $"Unexpected stop_reason: {stop}.");
        }

        // Thinking blocks may come first; the structured answer is the first text block.
        var text = root.GetProperty("content").EnumerateArray()
            .FirstOrDefault(b => b.GetProperty("type").GetString() == "text");
        if (text.ValueKind != JsonValueKind.Object)
        {
            return Answer(MatchDecision.Error, null, null, "No text block in the response.");
        }

        JsonElement answer;
        try
        {
            answer = JsonDocument.Parse(text.GetProperty("text").GetString()!).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Answer(MatchDecision.Error, null, null, $"Answer is not valid JSON: {ex.Message}");
        }

        var decision = answer.GetProperty("decision").GetString();
        int? bookId = answer.TryGetProperty("bookId", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : null;
        var confidence = answer.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
            ? Math.Round(Math.Clamp(c.GetDecimal(), 0m, 1m), 3)
            : (decimal?)null;
        var reason = answer.TryGetProperty("reason", out var r) ? r.GetString() : null;

        return decision switch
        {
            "match" when bookId is null || request.Candidates.All(x => x.BookId != bookId) =>
                Answer(MatchDecision.Error, null, confidence,
                    $"Model chose bookId {bookId?.ToString(CultureInfo.InvariantCulture) ?? "null"}, which was not a candidate. {reason}"),
            "match" when confidence < MinMatchConfidence => Answer(MatchDecision.Unsure, bookId, confidence, reason),
            "match" => Answer(MatchDecision.Match, bookId, confidence, reason),
            "new_book" => Answer(MatchDecision.NewBook, null, confidence, reason),
            "unsure" => Answer(MatchDecision.Unsure, null, confidence, reason),
            _ => Answer(MatchDecision.Error, null, confidence, $"Unknown decision '{decision}'."),
        };
    }
}
