using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class ScrapeRunConfiguration : IEntityTypeConfiguration<ScrapeRun>
{
    public void Configure(EntityTypeBuilder<ScrapeRun> builder)
    {
        builder.ToTable("ScrapeRun", Schemas.Ingest);
        builder.HasKey(x => x.ScrapeRunId);

        builder.Property(x => x.ScopeKey).HasMaxLength(200).IsUnicode(false);
        builder.Property(x => x.Query).HasMaxLength(200);
        builder.Property(x => x.EurRsdRate).HasPrecision(9, 4);
        builder.Property(x => x.RequestedBy).HasMaxLength(64);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.HangfireJobId).HasMaxLength(64).IsUnicode(false);

        builder.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId);

        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_ScrapeRun_IdempotencyKey");

        // FR-3: at most one Queued/Running run per source, enforced by the database.
        builder.HasIndex(x => x.SourceId)
            .IsUnique()
            .HasFilter("[Status] IN (0, 1)")
            .HasDatabaseName("UX_ScrapeRun_ActivePerSource");

        builder.HasIndex(x => new { x.SourceId, x.CreatedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ScrapeRun_Source_Created");
    }
}
