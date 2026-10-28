using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Sql";

    public static IServiceCollection AddPriceRadarInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set it with " +
                $"'dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"...\"' or the " +
                $"ConnectionStrings__{ConnectionStringName} environment variable (spec §10.1).");
        }

        services.AddDbContext<PriceRadarDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));

        return services;
    }

    /// <summary>Applies pending EF Core migrations (D-25: only the Api does this, and only when enabled).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PriceRadarDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, pending);
        await db.Database.MigrateAsync(ct);
    }
}
