using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PriceRadar.Core.Abstractions;
using PriceRadar.Infrastructure.Ingestion;
using PriceRadar.Infrastructure.Jobs;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;
using PriceRadar.Infrastructure.Sources;
using PriceRadar.Infrastructure.Staging;

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

        services.AddSingleton(TimeProvider.System);
        services.AddOptions<PricingSettings>().Bind(configuration.GetSection(PricingSettings.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SoldSettings>().Bind(configuration.GetSection(SoldSettings.Section)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(new StagingConnectionString(connectionString));
        services.AddTransient<StagingBulkWriter>();
        services.AddScoped<IngestionService>();
        services.AddPriceRadarHangfire(connectionString);

        // Fixture-backed sources (spec §4.5). The live Kupindo source is registered separately and stays disabled.
        var fixturesRoot = FixturePaths.Resolve(configuration["Sources:FixturesPath"]);
        services.AddSingleton<IListingSource>(new FixtureListingSource("fixture", fixturesRoot));
        services.AddSingleton<IListingSource>(new FixtureListingSource("kp", fixturesRoot));
        services.AddSingleton<ListingSourceRegistry>();

        return services;
    }

    /// <summary>Applies pending EF Core migrations (D-25: only the Api does this, and only when enabled).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PriceRadarDbContext>();
        await WaitUntilDatabaseOnlineOrAbsentAsync(db.Database.GetConnectionString()!, logger, ct);
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, pending);
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// After a SQL Server restart the server accepts logins (so the container is "healthy") while the database is
    /// still recovering. EF then cannot open it, concludes it does not exist and fails on CREATE DATABASE (error 1801).
    /// Wait until the database is either absent (fresh volume) or ONLINE.
    /// </summary>
    private static async Task WaitUntilDatabaseOnlineOrAbsentAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        var deadline = DateTime.UtcNow.AddMinutes(2);

        while (true)
        {
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync(ct);
            await using var command = new SqlCommand("SELECT state_desc FROM sys.databases WHERE name = @name", connection);
            command.Parameters.AddWithValue("@name", target.InitialCatalog);
            var state = (string?)await command.ExecuteScalarAsync(ct);

            if (state is null or "ONLINE")
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Database '{target.InitialCatalog}' stayed in state {state} for 2 minutes.");
            }

            logger.LogWarning("Database {Database} is {State}; waiting before applying migrations", target.InitialCatalog, state);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
