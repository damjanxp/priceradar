using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace PriceRadar.Infrastructure.Jobs;

public static class HangfireQueues
{
    public const string Ingest = "ingest";
    public const string Match = "match";
    public const string Load = "load";

    public static readonly string[] All = [Ingest, Match, Load];
}

public static class HangfireSetup
{
    public const string Schema = "hangfire";

    /// <summary>
    /// Hangfire storage shared by the Api (client + dashboard) and the Worker (server), spec §9.1. Jobs receive only
    /// the run id; every job reloads its state from the database.
    /// </summary>
    public static IServiceCollection AddPriceRadarHangfire(this IServiceCollection services, string connectionString)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(() => new SqlConnection(connectionString), new SqlServerStorageOptions
            {
                SchemaName = Schema,
                PrepareSchemaIfNecessary = true,
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.FromSeconds(1),
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            })
            .UseFilter(new RunStateFilter(connectionString)));

        services.AddScoped<ScrapeJob>();
        services.AddScoped<MatchJob>();
        services.AddScoped<ResolveJob>();
        services.AddScoped<LoadJob>();
        return services;
    }
}
