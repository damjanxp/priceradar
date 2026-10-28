using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence;

public class PriceRadarDbContext(DbContextOptions<PriceRadarDbContext> options) : DbContext(options)
{
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();
    public DbSet<StagingListing> StagingListings => Set<StagingListing>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<DimDate> DimDates => Set<DimDate>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<MatchCacheEntry> MatchCache => Set<MatchCacheEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are UTC datetime2(3) (spec §1.2).
        configurationBuilder.Properties<DateTime>().HavePrecision(3);

        // Every index in the schema is declared explicitly (spec §5.2/§5.4). EF's automatic FK indexes would
        // add an index on dw.PriceSnapshot(BookId) and spoil the DAY-6 "before" benchmark.
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PriceRadarDbContext).Assembly);

        // No cascading deletes: warehouse rows are never removed through their parents.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
