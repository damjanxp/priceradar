using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    private static readonly DateTime SeededAtUtc = new(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("Source", Schemas.Ingest);
        builder.HasKey(x => x.SourceId);
        builder.Property(x => x.SourceId).ValueGeneratedNever();

        builder.Property(x => x.Code).HasMaxLength(32).IsUnicode(false);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.BaseUrl).HasMaxLength(200).IsUnicode(false);

        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_Source_Code");

        // Live sources ship disabled (spec §4.5). KupujemProdajem forbids scraping, so "kp" replays invented
        // fixtures instead of being a live source (D-39).
        builder.HasData(
            new Source { SourceId = 1, Code = "fixture", Name = "Fixture files", Kind = SourceKind.Fixture, IsEnabled = true, MinDelayMs = 0, CreatedAtUtc = SeededAtUtc },
            new Source { SourceId = 2, Code = "synthetic", Name = "Synthetic generator", Kind = SourceKind.Synthetic, IsEnabled = true, MinDelayMs = 0, CreatedAtUtc = SeededAtUtc },
            new Source { SourceId = 3, Code = "kupindo", Name = "Kupindo", Kind = SourceKind.Live, BaseUrl = "https://www.kupindo.com", IsEnabled = false, MinDelayMs = 4000, CreatedAtUtc = SeededAtUtc },
            new Source { SourceId = 4, Code = "kp", Name = "KupujemProdajem-style fixtures", Kind = SourceKind.Fixture, IsEnabled = true, MinDelayMs = 0, CreatedAtUtc = SeededAtUtc });
    }
}
