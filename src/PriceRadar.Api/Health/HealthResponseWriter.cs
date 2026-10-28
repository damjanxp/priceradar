using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PriceRadar.Api.Health;

public static class HealthTags
{
    public const string Ready = "ready";
}

/// <summary>Writes health reports as <c>{"status":"Healthy","totalDurationMs":12,"entries":{"sql":{...}}}</c>.</summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                    description = e.Value.Description ?? e.Value.Exception?.Message,
                }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
