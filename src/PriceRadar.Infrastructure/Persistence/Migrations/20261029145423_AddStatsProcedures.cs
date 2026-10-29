using Microsoft.EntityFrameworkCore.Migrations;
using PriceRadar.Infrastructure.Sql;

#nullable disable

namespace PriceRadar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatsProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Read-side procedures (spec §5.3). No index changes: the DAY-4 baseline measures these against the
            // primary key only; IX_PriceSnapshot_Book_Date is the DAY-6 "after" step.
            migrationBuilder.Sql(SqlResource.Load("Procedures/dw.usp_GetBookStats.sql"));
            migrationBuilder.Sql(SqlResource.Load("Procedures/dw.usp_GetPriceHistory.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dw.usp_GetPriceHistory;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dw.usp_GetBookStats;");
        }
    }
}
