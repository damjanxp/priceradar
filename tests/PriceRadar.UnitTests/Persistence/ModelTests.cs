using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Persistence;
using PriceRadar.Infrastructure.Sql;

namespace PriceRadar.UnitTests.Persistence;

/// <summary>Guards the EF model against drifting away from spec §5.2 (no database needed).</summary>
public class ModelTests
{
    // The design-time model keeps everything migrations see, including seed data.
    private static readonly IModel Model = CreateContext().GetService<IDesignTimeModel>().Model;

    private static PriceRadarDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PriceRadarDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options);

    [Theory]
    [InlineData(typeof(Source), "ingest", "Source")]
    [InlineData(typeof(ScrapeRun), "ingest", "ScrapeRun")]
    [InlineData(typeof(StagingListing), "ingest", "StagingListing")]
    [InlineData(typeof(Book), "dw", "Book")]
    [InlineData(typeof(DimDate), "dw", "DimDate")]
    [InlineData(typeof(Listing), "dw", "Listing")]
    [InlineData(typeof(PriceSnapshot), "dw", "PriceSnapshot")]
    [InlineData(typeof(MatchCacheEntry), "match", "MatchCache")]
    public void Entity_MapsToSpecTable(Type clrType, string schema, string table)
    {
        var entity = Model.FindEntityType(clrType).ShouldNotBeNull();

        entity.GetSchema().ShouldBe(schema);
        entity.GetTableName().ShouldBe(table);
    }

    [Fact]
    public void Model_HasExactlyTheEightSpecTables()
    {
        Model.GetEntityTypes().Count().ShouldBe(8);
    }

    [Fact]
    public void PriceSnapshot_HasNoBookIndexBeforeDay6()
    {
        // The DAY-6 benchmark measures adding IX_PriceSnapshot_Book_Date; nothing may pre-empt it.
        var indexes = Model.FindEntityType(typeof(PriceSnapshot))!.GetIndexes();

        indexes.ShouldNotContain(i => i.Properties[0].Name == nameof(PriceSnapshot.BookId));
        indexes.Select(i => i.GetDatabaseName()).ShouldBe(["UX_PriceSnapshot_Listing_Run"]);
    }

    [Fact]
    public void ScrapeRun_ActiveRunIndexIsFilteredAndUnique()
    {
        var index = Model.FindEntityType(typeof(ScrapeRun))!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "UX_ScrapeRun_ActivePerSource");

        index.IsUnique.ShouldBeTrue();
        index.GetFilter().ShouldBe("[Status] IN (0, 1)");
    }

    [Fact]
    public void DateTimeColumns_AreDatetime2WithMillisecondPrecision()
    {
        var dateTimes = Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .ToList();

        dateTimes.ShouldNotBeEmpty();
        dateTimes.ShouldAllBe(p => p.GetPrecision() == 3);
    }

    [Fact]
    public void ForeignKeys_NeverCascade()
    {
        Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .ShouldAllBe(fk => fk.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Sources_SeedFourRowsWithLiveSourcesDisabled()
    {
        var seed = Model.FindEntityType(typeof(Source))!.GetSeedData().ToList();

        seed.Count.ShouldBe(4);
        seed.Where(s => (SourceKind)s[nameof(Source.Kind)]! == SourceKind.Live)
            .ShouldAllBe(s => (bool)s[nameof(Source.IsEnabled)]! == false);
    }

    [Fact]
    public void Sources_KupujemProdajemIsFixtureOnly()
    {
        // D-39: KP's terms forbid scraping; it must never be seeded as a live source.
        var kp = Model.FindEntityType(typeof(Source))!.GetSeedData().Single(s => (string)s[nameof(Source.Code)]! == "kp");

        ((SourceKind)kp[nameof(Source.Kind)]!).ShouldBe(SourceKind.Fixture);
        kp[nameof(Source.BaseUrl)].ShouldBeNull();
    }

    [Fact]
    public void DimDateSeedScript_IsEmbedded()
    {
        SqlResource.Load("Seed/dw.DimDate.sql").ShouldContain("INSERT INTO dw.DimDate");
    }
}
