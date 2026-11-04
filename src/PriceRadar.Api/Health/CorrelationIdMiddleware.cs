using System.Diagnostics;
using Serilog.Context;

namespace PriceRadar.Api.Health;

/// <summary>
/// Every request carries a correlation id (spec NFR-6): the caller's <c>X-Correlation-Id</c> if it sent a sane one,
/// otherwise the W3C trace id — the same value the ProblemDetails <c>traceId</c> contains. It is echoed in the response
/// and pushed into every log line of the request, so one id finds a client's error in the logs.
/// </summary>
public static class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var incoming = context.Request.Headers[HeaderName].ToString();
            var id = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                ? incoming
                : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = id;
                return Task.CompletedTask;
            });

            using (LogContext.PushProperty("CorrelationId", id))
            {
                await next();
            }
        });
}
