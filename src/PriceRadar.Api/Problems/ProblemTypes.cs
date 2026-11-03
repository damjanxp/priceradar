using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PriceRadar.Api.Problems;

/// <summary>RFC 9457 problem types used by the API (spec §8.3).</summary>
public static class ProblemTypes
{
    private const string Base = "https://priceradar.dev/problems/";

    public const string Validation = Base + "validation";
    public const string Unauthorized = Base + "unauthorized";
    public const string NotFound = Base + "not-found";
    public const string RunAlreadyActive = Base + "run-already-active";
    public const string SourceDisabled = Base + "source-disabled";
    public const string RateLimited = Base + "rate-limited";
    public const string Unexpected = Base + "unexpected";

    public static string TraceId(HttpContext http) => Activity.Current?.Id ?? http.TraceIdentifier;

    public static ProblemDetails Create(HttpContext http, int status, string type, string title, string? detail) =>
        new()
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = http.Request.Path,
            Extensions = { ["traceId"] = TraceId(http) },
        };

    /// <summary>
    /// Makes every ProblemDetails the framework writes (validation, 404 from routing, 500) look like ours: a
    /// priceradar.dev type, the request path and a traceId that also appears in the logs.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Instance ??= context.HttpContext.Request.Path;
        problem.Extensions.TryAdd("traceId", TraceId(context.HttpContext));
        if (problem.Type is null || problem.Type.StartsWith("https://tools.ietf.org/", StringComparison.Ordinal))
        {
            problem.Type = problem.Status switch
            {
                400 => Validation,
                401 => Unauthorized,
                404 => NotFound,
                429 => RateLimited,
                _ => Unexpected,
            };
        }

        if (problem.Status == 500)
        {
            // Never leak exception details; the traceId finds the full error in the logs.
            problem.Title = "An unexpected error occurred.";
            problem.Detail = "The error was logged; quote the traceId when reporting it.";
        }
    }
}
