using Hangfire;
using PriceRadar.Infrastructure;
using PriceRadar.Infrastructure.Jobs;
using Serilog;

// Hosts the Hangfire server that runs the ingestion pipeline (spec §9.1). The Api enqueues; this process executes.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "PriceRadar.Worker"));

    builder.Services.AddPriceRadarInfrastructure(builder.Configuration);
    builder.Services.AddHangfireServer(options =>
    {
        options.ServerName = $"priceradar-worker:{Environment.MachineName}";
        options.Queues = HangfireQueues.All;
        options.WorkerCount = 2;
    });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PriceRadar.Worker terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
