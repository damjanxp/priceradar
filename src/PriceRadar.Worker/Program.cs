using PriceRadar.Infrastructure;
using Serilog;

// The Hangfire server is added on DAY-3 (spec §9.1). Until then the Worker only proves it can start
// with the shared configuration and infrastructure registrations.
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
