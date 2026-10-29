using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PriceRadar.Infrastructure.Persistence;
using Respawn;
using Testcontainers.MsSql;

namespace PriceRadar.IntegrationTests.Infrastructure;

/// <summary>
/// One real SQL Server 2022 container per test run with every migration applied (spec §11.1, D-27).
/// <see cref="ResetAsync"/> wipes the pipeline data between tests and keeps the schema and reference data.
/// </summary>
public sealed class MsSqlFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private Respawner? _respawner;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "PriceRadarTests",
            TrustServerCertificate = true,
        }.ConnectionString;

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        _respawner = await Respawner.CreateAsync(ConnectionString, new RespawnerOptions
        {
            SchemasToInclude = ["ingest", "dw", "match"],
            TablesToIgnore = ["__EFMigrationsHistory", new Respawn.Graph.Table("ingest", "Source"), new Respawn.Graph.Table("dw", "DimDate")],
            WithReseed = true,
        });
    }

    public Task ResetAsync() => _respawner!.ResetAsync(ConnectionString);

    public PriceRadarDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<PriceRadarDbContext>().UseSqlServer(ConnectionString).Options);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<MsSqlFixture>
{
    public const string Name = "sql";
}
