using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PriceRadar.Infrastructure;
using PriceRadar.Seeder;

const string Usage = """
    PriceRadar.Seeder — synthetic data and fixture staging (spec §11.3)

      seed           [--books 20000] [--listings 60000] [--snapshots 500000] [--days 180] [--seed 42]
                     [--end-date yyyy-MM-dd] [--reset]
      stage-fixture  --set run-001 [--source fixture|kp]

    The connection string is read from the Api's user-secrets (ConnectionStrings:Sql) or the
    ConnectionStrings__Sql environment variable.
    """;

var cli = CliArgs.Parse(args);
if (cli.Command is null or "help" or "--help")
{
    Console.WriteLine(Usage);
    return cli.Command is null ? 1 : 0;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
builder.Configuration.AddUserSecrets(SeederSecrets.ApiUserSecretsId).AddEnvironmentVariables();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddPriceRadarInfrastructure(builder.Configuration);
builder.Services.AddTransient<SeedCommand>();
builder.Services.AddTransient<StageFixtureCommand>();

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    return cli.Command switch
    {
        "seed" => await host.Services.GetRequiredService<SeedCommand>().RunAsync(SeedOptions.From(cli), cts.Token),
        "stage-fixture" => await host.Services.GetRequiredService<StageFixtureCommand>().RunAsync(cli, cts.Token),
        _ => Fail($"Unknown command '{cli.Command}'.\n\n{Usage}"),
    };
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException or KeyNotFoundException)
{
    return Fail(ex.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 2;
}

internal static class SeederSecrets
{
    // Same store as PriceRadar.Api, so the connection string is configured in one place.
    public const string ApiUserSecretsId = "priceradar-api-3f6c1a2e";
}
