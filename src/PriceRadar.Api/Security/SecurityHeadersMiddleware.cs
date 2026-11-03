namespace PriceRadar.Api.Security;

/// <summary>Baseline security headers (spec §10.3). The strict CSP is skipped for the tool UIs that need inline scripts.</summary>
public static class SecurityHeadersMiddleware
{
    private static readonly string[] ToolPaths = ["/scalar", "/hangfire", "/openapi"];

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                if (!ToolPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
                {
                    headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";
                }

                return Task.CompletedTask;
            });
            await next();
        });
}
