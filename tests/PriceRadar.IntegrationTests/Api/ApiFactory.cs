using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PriceRadar.IntegrationTests.Infrastructure;

namespace PriceRadar.IntegrationTests.Api;

/// <summary>The real Api (all middleware, filters and DI) against the Testcontainers database.</summary>
public sealed class ApiFactory(MsSqlFixture sql, int lookupPerMinute = 1_000) : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sql", sql.ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");   // the fixture migrated already
        builder.UseSetting("Security:ApiKey", ApiKey);
        builder.UseSetting("RateLimiting:LookupPerMinute", lookupPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:IngestionPerMinute", "1000");
    }
}
