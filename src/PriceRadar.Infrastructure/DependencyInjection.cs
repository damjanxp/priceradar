using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceRadar.Core.Abstractions;
using PriceRadar.Infrastructure.Ai;
using PriceRadar.Infrastructure.Ingestion;
using PriceRadar.Infrastructure.Jobs;
using PriceRadar.Infrastructure.Matching;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;
using PriceRadar.Infrastructure.Queries;
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
        services.AddOptions<MatchingSettings>().Bind(configuration.GetSection(MatchingSettings.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AiSettings>().Bind(configuration.GetSection(AiSettings.Section)).ValidateDataAnnotations().ValidateOnStart();

        // Claude client: typed HttpClient + standard resilience (retry 429/5xx with jitter, honours Retry-After), D-16.
        var aiTimeout = TimeSpan.FromSeconds(configuration.GetValue($"{AiSettings.Section}:TimeoutSeconds", 20));
        services.AddHttpClient<IAiMatchResolver, ClaudeMatchResolver>((sp, http) =>
            {
                http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<AiSettings>>().Value.BaseUrl);
                http.Timeout = Timeout.InfiniteTimeSpan;   // the resilience pipeline owns all timeouts
            })
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = aiTimeout;
                o.TotalRequestTimeout.Timeout = aiTimeout * 4;
                o.CircuitBreaker.SamplingDuration = aiTimeout * 2;
                o.Retry.MaxRetryAttempts = 3;
            });
        services.AddScoped<MatchCacheService>();
        services.AddSingleton<BookCatalogProvider>();

        services.AddSingleton(new StagingConnectionString(connectionString));
        services.AddTransient<StagingBulkWriter>();
        services.AddScoped<IngestionService>();
        services.AddTransient<BookStatsQuery>();
        services.AddTransient<BookSearchQuery>();
        services.AddTransient<CatalogQuery>();
        services.AddScoped<LookupService>();
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

            if (state is null)
            {
                return;   // fresh volume: EF creates the database
            }

            // ONLINE in sys.databases is not enough: right after a restart, logins to the database can still fail for a
            // moment (error 4060), and EF would again decide the database is missing. Open it for real.
            if (state == "ONLINE" && await CanOpenAsync(connectionString, ct))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Database '{target.InitialCatalog}' stayed in state {state} for 2 minutes.");
            }

            logger.LogWarning("Database {Database} is {State} but not yet accepting connections; waiting before applying migrations",
                target.InitialCatalog, state);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private static async Task<bool> CanOpenAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var probe = new SqlCommand("SELECT 1", connection);
            await probe.ExecuteScalarAsync(ct);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
