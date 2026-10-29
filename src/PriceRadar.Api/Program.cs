using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PriceRadar.Api.Health;
using PriceRadar.Infrastructure;
using PriceRadar.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;

// Two-stage init: a bootstrap logger catches startup failures before configuration is read.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "PriceRadar.Api"));

    builder.Services.AddPriceRadarInfrastructure(builder.Configuration);
    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<PriceRadarDbContext>("sql", tags: [HealthTags.Ready])
        .AddCheck<HangfireStorageHealthCheck>("hangfire", tags: [HealthTags.Ready]);

    var app = builder.Build();

    if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
    {
        await app.Services.MigrateDatabaseAsync(app.Logger);
    }

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    // Liveness: the process answers. Readiness: dependencies (SQL; Hangfire storage from DAY-3) answer.
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains(HealthTags.Ready),
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });

    // The Api only enqueues jobs and shows the dashboard; the Worker runs them (spec §9.1).
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        DashboardTitle = "PriceRadar jobs",
        Authorization = [new DashboardAuthorization(app.Environment)],
        IsReadOnlyFunc = _ => !app.Environment.IsDevelopment(),
    });

    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PriceRadar.Api terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
