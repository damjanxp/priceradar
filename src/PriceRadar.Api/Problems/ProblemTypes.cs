using Microsoft.AspNetCore.Mvc;

namespace PriceRadar.Api.Problems;

/// <summary>RFC 9457 problem types used by the API (spec §8.3).</summary>
public static class ProblemTypes
{
    private const string Base = "https://priceradar.dev/problems/";

    public const string Validation = Base + "validation";
    public const string NotFound = Base + "not-found";
    public const string RunAlreadyActive = Base + "run-already-active";
    public const string SourceDisabled = Base + "source-disabled";

    public static ProblemDetails Create(HttpContext http, int status, string type, string title, string? detail) =>
        new()
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = http.Request.Path,
            Extensions = { ["traceId"] = System.Diagnostics.Activity.Current?.Id ?? http.TraceIdentifier },
        };
}
