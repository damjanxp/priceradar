using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class MatchCacheEntryConfiguration : IEntityTypeConfiguration<MatchCacheEntry>
{
    public void Configure(EntityTypeBuilder<MatchCacheEntry> builder)
    {
        builder.ToTable("MatchCache", Schemas.Match);
        builder.HasKey(x => x.MatchCacheId);

        builder.Property(x => x.InputHash).HasColumnType("binary(32)");
        builder.Property(x => x.PromptVersion).HasMaxLength(16).IsUnicode(false);
        builder.Property(x => x.NormTitle).HasMaxLength(300).IsUnicode(false);
        builder.Property(x => x.NormAuthor).HasMaxLength(200).IsUnicode(false);
        builder.Property(x => x.CandidateBookIds).HasMaxLength(200).IsUnicode(false);
        builder.Property(x => x.Confidence).HasPrecision(4, 3);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.Model).HasMaxLength(64).IsUnicode(false);

        builder.HasOne<Book>().WithMany().HasForeignKey(x => x.ResolvedBookId);

        builder.HasIndex(x => x.InputHash).IsUnique().HasDatabaseName("UX_MatchCache_InputHash");
    }
}
