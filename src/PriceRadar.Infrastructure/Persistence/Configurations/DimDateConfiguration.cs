using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceRadar.Core.Domain;

namespace PriceRadar.Infrastructure.Persistence.Configurations;

internal sealed class DimDateConfiguration : IEntityTypeConfiguration<DimDate>
{
    public void Configure(EntityTypeBuilder<DimDate> builder)
    {
        builder.ToTable("DimDate", Schemas.Warehouse);
        builder.HasKey(x => x.DateKey);
        builder.Property(x => x.DateKey).ValueGeneratedNever();
        builder.Property(x => x.Date).HasColumnType("date");

        builder.HasIndex(x => x.Date).IsUnique().HasDatabaseName("UX_DimDate_Date");
    }
}
