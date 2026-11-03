using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PriceRadar.Api.Problems;

namespace PriceRadar.Api.Security;

/// <summary><c>Security</c> section (spec §10.1). An empty <see cref="ApiKey"/> disables the check.</summary>
public sealed class SecuritySettings
{
    public const string Section = "Security";
    public const string HeaderName = "X-Api-Key";

    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Protects write endpoints with the <c>X-Api-Key</c> header when a key is configured (D-29). JWT is Stretch (S-3).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute() : TypeFilterAttribute(typeof(ApiKeyFilter));

public sealed class ApiKeyFilter(IOptions<SecuritySettings> settings) : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = settings.Value.ApiKey;
        if (string.IsNullOrEmpty(expected))
        {
            return next();
        }

        var provided = context.HttpContext.Request.Headers[SecuritySettings.HeaderName].ToString();
        // Constant-time comparison, so response timing does not leak how much of the key matched.
        if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
        {
            return next();
        }

        var problem = ProblemTypes.Create(context.HttpContext, StatusCodes.Status401Unauthorized, ProblemTypes.Unauthorized,
            "A valid API key is required.", $"Send the key in the {SecuritySettings.HeaderName} header.");
        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status401Unauthorized };
        return Task.CompletedTask;
    }
}
