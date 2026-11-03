using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PriceRadar.Api.Problems;

namespace PriceRadar.Api.Security;

/// <summary><c>RateLimiting</c> section (spec §10.2): requests per minute per client IP.</summary>
public sealed class RateLimitSettings
{
    public const string Section = "RateLimiting";

    public int LookupPerMinute { get; set; } = 60;
    public int IngestionPerMinute { get; set; } = 5;
}

public static class RateLimitPolicies
{
    public const string Lookup = "lookup";
    public const string Ingestion = "ingestion";

    public static IServiceCollection AddPriceRadarRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitSettings.Section).Get<RateLimitSettings>() ?? new RateLimitSettings();

        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Lookup, http => FixedWindowPerIp(http, settings.LookupPerMinute));
            options.AddPolicy(Ingestion, http => FixedWindowPerIp(http, settings.IngestionPerMinute));
            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problem = ProblemTypes.Create(http, 429, ProblemTypes.RateLimited, "Too many requests.",
                    "The rate limit for this endpoint was exceeded; retry after the time in the Retry-After header.");
                await http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", ct);
            };
        });
    }

    private static RateLimitPartition<string> FixedWindowPerIp(HttpContext http, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
}
