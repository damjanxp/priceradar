using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using PriceRadar.Api.Health;
using PriceRadar.Api.Problems;
using PriceRadar.Api.Security;
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
    builder.Services.AddControllers(o =>
            // Validation errors are keyed by the JSON name the client sent ("maxPages"), not the C# name ("MaxPages").
            o.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
        // Enums as snake_case strings, as in spec §8: "cheap", "insufficient_data", "no_match", "succeeded".
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemTypes.Customize);
    builder.Services.Configure<SecuritySettings>(builder.Configuration.GetSection(SecuritySettings.Section));
    builder.Services.AddPriceRadarRateLimiting(builder.Configuration);

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<PriceRadarDbContext>("sql", tags: [HealthTags.Ready])
        .AddCheck<HangfireStorageHealthCheck>("hangfire", tags: [HealthTags.Ready]);

    var app = builder.Build();

    if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
    {
        await app.Services.MigrateDatabaseAsync(app.Logger);
    }

    app.UseCorrelationId();
    app.UseSerilogRequestLogging();
    app.UseSecurityHeaders();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseRateLimiter();

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
