using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("Listing", Schemas.Warehouse);
        builder.HasKey(x => x.ListingId);

        builder.Property(x => x.ExternalId).HasMaxLength(64).IsUnicode(false);
        builder.Property(x => x.MatchScore).HasPrecision(5, 4);
        builder.Property(x => x.Title).HasMaxLength(400);
        builder.Property(x => x.Condition).HasMaxLength(100);
        builder.Property(x => x.Url).HasMaxLength(500).IsUnicode(false);
        builder.Property(x => x.ScopeKey).HasMaxLength(200).IsUnicode(false);
        builder.Property(x => x.CurrentPriceRsd).HasPrecision(12, 2);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId);
        builder.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId);
        builder.HasOne(x => x.LastSeenRun).WithMany().HasForeignKey(x => x.LastSeenRunId);

        builder.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique().HasDatabaseName("UX_Listing_Source_External");

        builder.HasIndex(x => new { x.BookId, x.Status })
            .IncludeProperties(x => new { x.CurrentPriceRsd, x.LastSeenAtUtc, x.DisappearedAtUtc })
            .HasDatabaseName("IX_Listing_Book_Status");

        builder.HasIndex(x => new { x.SourceId, x.ScopeKey, x.Status })
            .IncludeProperties(x => new { x.LastSeenRunId, x.MissedRunCount })
            .HasDatabaseName("IX_Listing_Scope_Status");
    }
}
