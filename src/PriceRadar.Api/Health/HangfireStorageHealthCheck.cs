using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PriceRadar.Api.Health;

/// <summary>Readiness: Hangfire storage answers and at least one Worker server is registered (spec FR-19).</summary>
public sealed class HangfireStorageHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var api = storage.GetMonitoringApi();
            var servers = api.Servers();
            var stats = api.GetStatistics();
            var data = new Dictionary<string, object> { ["servers"] = servers.Count, ["enqueued"] = stats.Enqueued, ["failed"] = stats.Failed };

            return Task.FromResult(servers.Count > 0
                ? HealthCheckResult.Healthy($"{servers.Count} server(s), {stats.Enqueued} enqueued", data)
                : HealthCheckResult.Degraded("Storage reachable, but no Hangfire server (Worker) is running", data: data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage unreachable", ex));
        }
    }
}
