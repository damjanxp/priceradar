using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Ai;
using PriceRadar.Infrastructure.Options;

namespace PriceRadar.UnitTests.Ai;

/// <summary>The Claude client against a fake HTTP handler: request shape and every stop_reason branch (spec §6.5).</summary>
public class ClaudeMatchResolverTests
{
    private static readonly AiMatchRequest Request = new("НА ДРИНИ ЋУПРИЈА - kao nova", "I. Andrić", "na drini cuprija", "andric i",
    [
        new AiCandidate(1201, "Na Drini ćuprija", "Ivo Andrić", 0.88),
        new AiCandidate(3307, "Na Drini ćuprija (strip)", "Zoran Penevski", 0.71),
    ]);

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Sent { get; private set; }
        public string? SentBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent = request;
            SentBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (ClaudeMatchResolver Resolver, FakeHandler Handler) Create(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test/") };
        var settings = Microsoft.Extensions.Options.Options.Create(new AiSettings { ApiKey = "test-key" });
        return (new ClaudeMatchResolver(http, settings, NullLogger<ClaudeMatchResolver>.Instance), handler);
    }

    private static string Response(string answerJson, string stopReason = "end_turn", bool withThinking = true)
    {
        var content = new JsonArray();
        if (withThinking)
        {
            content.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "sig" });
        }

        content.Add(new JsonObject { ["type"] = "text", ["text"] = answerJson });
        return new JsonObject
        {
            ["id"] = "msg_1",
            ["type"] = "message",
            ["model"] = "claude-haiku-5-5",
            ["content"] = content,
            ["stop_reason"] = stopReason,
            ["usage"] = new JsonObject { ["input_tokens"] = 612, ["output_tokens"] = 148 },
        }.ToJsonString();
    }

    [Fact]
    public async Task Request_HasSchemaEffortHeadersAndNoSamplingParameters()
    {
        var (resolver, handler) = Create(Response("""{"decision":"match","bookId":1201,"confidence":0.93,"reason":"same work"}"""));

        await resolver.ResolveAsync(Request, CancellationToken.None);

        handler.Sent!.RequestUri!.ToString().ShouldBe("https://api.anthropic.test/v1/messages");
        handler.Sent.Headers.GetValues("anthropic-version").Single().ShouldBe("2026-10-21");
        handler.Sent.Headers.GetValues("x-api-key").Single().ShouldBe("test-key");

        var body = JsonNode.Parse(handler.SentBody!)!.AsObject();
        body["model"]!.GetValue<string>().ShouldBe("claude-haiku-5-5");
        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("low");
        body["output_config"]!["format"]!["type"]!.GetValue<string>().ShouldBe("json_schema");
        body.ContainsKey("temperature").ShouldBeFalse();   // rejected by API version 2026-10-21
        body.ContainsKey("top_p").ShouldBeFalse();

        var input = JsonNode.Parse(body["messages"]![0]!["content"]!.GetValue<string>())!;
        input["candidates"]!.AsArray().Count.ShouldBe(2);
        input["listing"]!["title"]!.GetValue<string>().ShouldBe("НА ДРИНИ ЋУПРИЈА - kao nova");
    }

    [Fact]
    public async Task ValidMatch_AfterThinkingBlock_IsParsedWithUsage()
    {
        var (resolver, _) = Create(Response("""{"decision":"match","bookId":1201,"confidence":0.93,"reason":"same work"}"""));

        var answer = await resolver.ResolveAsync(Request, CancellationToken.None);

        answer.ShouldSatisfyAllConditions(
            a => a.Decision.ShouldBe(MatchDecision.Match),
            a => a.BookId.ShouldBe(1201),
            a => a.Confidence.ShouldBe(0.93m),
            a => (a.InputTokens, a.OutputTokens).ShouldBe((612, 148)));
    }

    [Fact]
    public async Task BookIdNotAmongCandidates_IsAnError()
    {
        var (resolver, _) = Create(Response("""{"decision":"match","bookId":999,"confidence":0.97,"reason":"guess"}"""));

        var answer = await resolver.ResolveAsync(Request, CancellationToken.None);

        answer.Decision.ShouldBe(MatchDecision.Error);
        answer.BookId.ShouldBeNull();
        answer.Reason!.ShouldContain("999");
    }

    [Fact]
    public async Task LowConfidenceMatch_IsDowngradedToUnsure()
    {
        var (resolver, _) = Create(Response("""{"decision":"match","bookId":1201,"confidence":0.45,"reason":"maybe"}"""));

        (await resolver.ResolveAsync(Request, CancellationToken.None)).Decision.ShouldBe(MatchDecision.Unsure);
    }

    [Theory]
    [InlineData("new_book", MatchDecision.NewBook)]
    [InlineData("unsure", MatchDecision.Unsure)]
    public async Task NonMatchDecisions_AreMapped(string decision, MatchDecision expected)
    {
        var (resolver, _) = Create(Response($$"""{"decision":"{{decision}}","bookId":null,"confidence":0.8,"reason":"r"}"""));

        (await resolver.ResolveAsync(Request, CancellationToken.None)).Decision.ShouldBe(expected);
    }

    [Theory]
    [InlineData("max_tokens", MatchDecision.Error)]
    [InlineData("refusal", MatchDecision.Unsure)]
    [InlineData("pause_turn", MatchDecision.Error)]
    public async Task StopReasons_AreHandled(string stopReason, MatchDecision expected)
    {
        var (resolver, _) = Create(Response("""{"decision":"match","bookId":1201,"confidence":0.9,"reason":"r"}""", stopReason));

        (await resolver.ResolveAsync(Request, CancellationToken.None)).Decision.ShouldBe(expected);
    }

    [Fact]
    public async Task MalformedAnswerText_IsAnError()
    {
        var (resolver, _) = Create(Response("not json", withThinking: false));

        (await resolver.ResolveAsync(Request, CancellationToken.None)).Decision.ShouldBe(MatchDecision.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ClientErrors_ThrowNonRetryableException(HttpStatusCode status)
    {
        var (resolver, _) = Create("""{"type":"error","error":{"type":"invalid_request_error","message":"temperature is not supported"}}""", status);

        var ex = await Should.ThrowAsync<AiResolverException>(() => resolver.ResolveAsync(Request, CancellationToken.None));
        ex.StatusCode.ShouldBe((int)status);
    }

    [Fact]
    public async Task RateLimitAfterRetries_ThrowsTransientException()
    {
        var (resolver, _) = Create("""{"type":"error","error":{"type":"rate_limit_error"}}""", HttpStatusCode.TooManyRequests);

        await Should.ThrowAsync<HttpRequestException>(() => resolver.ResolveAsync(Request, CancellationToken.None));
    }

    [Fact]
    public void CacheKey_DependsOnPromptVersionAndNormalisedText()
    {
        var v1 = MatchCacheService.Hash("v1", "na drini cuprija", "andric ivo");

        v1.Length.ShouldBe(32);
        MatchCacheService.Hash("v1", "na drini cuprija", "andric ivo").ShouldBe(v1);
        MatchCacheService.Hash("v2", "na drini cuprija", "andric ivo").ShouldNotBe(v1);
        MatchCacheService.Hash("v1", "na drini cuprija", "").ShouldNotBe(v1);
    }

    [Fact]
    public void CostUsd_UsesConfiguredPrices()
    {
        // 600 in + 150 out on Haiku 5.5 prices (0.10 / 0.50 USD per MTok) ≈ 0.000135 USD (spec §6.6).
        new AiSettings().CostUsd(600, 150).ShouldBe(0.000135m);
        JsonSerializer.Serialize(new AiSettings { ApiKey = "" }.IsActive).ShouldBe("false");
    }
}
