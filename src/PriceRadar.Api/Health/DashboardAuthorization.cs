using Hangfire.Dashboard;

namespace PriceRadar.Api.Health;

/// <summary>
/// The dashboard is open in Development (the local Docker stack, where requests come through the container gateway and
/// are never "local"); elsewhere only requests from the same machine are allowed. API-key protection: DAY-5 (spec §10.3).
/// </summary>
public sealed class DashboardAuthorization(IWebHostEnvironment environment) : IDashboardAuthorizationFilter
{
    private readonly LocalRequestsOnlyAuthorizationFilter _localOnly = new();

    public bool Authorize(DashboardContext context) => environment.IsDevelopment() || _localOnly.Authorize(context);
}
