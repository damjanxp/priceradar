using Microsoft.EntityFrameworkCore.Migrations;
using PriceRadar.Infrastructure.Sql;

#nullable disable

namespace PriceRadar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FixtureSet",
                schema: "ingest",
                table: "ScrapeRun",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            // Hand-added: pipeline SQL objects (spec §5.3), versioned as embedded .sql files. The type comes first;
            // each CREATE PROCEDURE runs in its own batch.
            migrationBuilder.Sql(SqlResource.Load("Types/ingest.MatchResultType.sql"));
            migrationBuilder.Sql(SqlResource.Load("Procedures/ingest.usp_ApplyMatchResults.sql"));
            migrationBuilder.Sql(SqlResource.Load("Procedures/dw.usp_LoadRun.sql"));
            migrationBuilder.Sql(SqlResource.Load("Procedures/dw.usp_UpdateListingPresence.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dw.usp_UpdateListingPresence;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dw.usp_LoadRun;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS ingest.usp_ApplyMatchResults;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS ingest.MatchResultType;");

            migrationBuilder.DropColumn(
                name: "FixtureSet",
                schema: "ingest",
                table: "ScrapeRun");
        }
    }
}
