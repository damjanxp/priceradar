using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class StagingListingConfiguration : IEntityTypeConfiguration<StagingListing>
{
    public void Configure(EntityTypeBuilder<StagingListing> builder)
    {
        builder.ToTable("StagingListing", Schemas.Ingest);
        builder.HasKey(x => x.StagingListingId);

        builder.Property(x => x.ExternalId).HasMaxLength(64).IsUnicode(false);

        // Raw text stays nvarchar so Cyrillic survives untouched.
        builder.Property(x => x.RawTitle).HasMaxLength(400);
        builder.Property(x => x.RawAuthor).HasMaxLength(200);
        builder.Property(x => x.RawPrice).HasMaxLength(50);
        builder.Property(x => x.RawCondition).HasMaxLength(100);
        builder.Property(x => x.Url).HasMaxLength(500).IsUnicode(false);
        builder.Property(x => x.PayloadHash).HasColumnType("binary(32)");

        // Normalised text is ASCII after folding (spec §6.1).
        builder.Property(x => x.PriceRsd).HasPrecision(12, 2);
        builder.Property(x => x.NormTitle).HasMaxLength(300).IsUnicode(false);
        builder.Property(x => x.NormAuthor).HasMaxLength(200).IsUnicode(false);
        builder.Property(x => x.MatchScore).HasPrecision(5, 4);
        builder.Property(x => x.RejectReason).HasMaxLength(50).IsUnicode(false);

        builder.HasOne(x => x.ScrapeRun).WithMany().HasForeignKey(x => x.ScrapeRunId);

        builder.HasIndex(x => new { x.ScrapeRunId, x.ExternalId }).IsUnique().HasDatabaseName("UX_Staging_Run_External");
        builder.HasIndex(x => new { x.ScrapeRunId, x.ProcessStatus }).HasDatabaseName("IX_Staging_Run_Status");
    }
}
