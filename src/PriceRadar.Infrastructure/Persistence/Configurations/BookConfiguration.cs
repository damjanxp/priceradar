using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Book", Schemas.Warehouse);
        builder.HasKey(x => x.BookId);

        builder.Property(x => x.Title).HasMaxLength(300);
        builder.Property(x => x.Author).HasMaxLength(200);
        builder.Property(x => x.NormTitle).HasMaxLength(300).IsUnicode(false);
        builder.Property(x => x.NormAuthor).HasMaxLength(200).IsUnicode(false).HasDefaultValue(string.Empty);

        builder.Property(x => x.NormKey)
            .HasMaxLength(501)
            .IsUnicode(false)
            .HasComputedColumnSql("[NormAuthor] + '|' + [NormTitle]", stored: true);

        builder.HasIndex(x => x.NormKey).IsUnique().HasDatabaseName("UX_Book_NormKey");
    }
}
