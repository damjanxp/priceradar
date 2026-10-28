using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class PriceSnapshotConfiguration : IEntityTypeConfiguration<PriceSnapshot>
{
    public void Configure(EntityTypeBuilder<PriceSnapshot> builder)
    {
        builder.ToTable("PriceSnapshot", Schemas.Warehouse);

        // Identity PK stays clustered: append-only inserts without page splits (D-10).
        builder.HasKey(x => x.PriceSnapshotId).IsClustered();

        builder.Property(x => x.PriceRsd).HasPrecision(12, 2);

        builder.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId);
        builder.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId);
        builder.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId);
        builder.HasOne<ScrapeRun>().WithMany().HasForeignKey(x => x.ScrapeRunId);
        builder.HasOne<DimDate>().WithMany().HasForeignKey(x => x.DateKey);

        // FR-10: at most one snapshot per listing per run.
        builder.HasIndex(x => new { x.ListingId, x.ScrapeRunId }).IsUnique().HasDatabaseName("UX_PriceSnapshot_Listing_Run");

        // IX_PriceSnapshot_Book_Date is deliberately absent until DAY-6: it is the measured "after" step.
    }
}
