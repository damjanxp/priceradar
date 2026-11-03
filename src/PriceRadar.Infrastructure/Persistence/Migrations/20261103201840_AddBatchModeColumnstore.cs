using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceRadar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchModeColumnstore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DAY-6 (BENCHMARKS.md, D-40): an EMPTY filtered columnstore index. The contradictory filter means it never
            // holds a row, so it costs nothing on insert, but its existence lets the optimiser run the stats and history
            // queries in batch mode: batch-mode Window Aggregate replaces the row-mode window spool (Q3: 50,742 → 56
            // logical reads, 41 → 10 ms). Not modelled in EF on purpose (EF cannot express it); dropped in Down.
            migrationBuilder.Sql(
                "CREATE NONCLUSTERED COLUMNSTORE INDEX NCCI_PriceSnapshot_BatchMode ON dw.PriceSnapshot (PriceSnapshotId) " +
                "WHERE PriceSnapshotId = -1 AND PriceSnapshotId = -2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS NCCI_PriceSnapshot_BatchMode ON dw.PriceSnapshot;");
        }
    }
}
