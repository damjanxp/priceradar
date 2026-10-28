using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceRadar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KpAsFixtureSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "ingest",
                table: "Source",
                keyColumn: "SourceId",
                keyValue: (byte)4,
                columns: new[] { "BaseUrl", "IsEnabled", "Kind", "MinDelayMs", "Name" },
                values: new object[] { null, true, (byte)0, 0, "KupujemProdajem-style fixtures" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "ingest",
                table: "Source",
                keyColumn: "SourceId",
                keyValue: (byte)4,
                columns: new[] { "BaseUrl", "IsEnabled", "Kind", "MinDelayMs", "Name" },
                values: new object[] { "https://www.kupujemprodajem.com", false, (byte)2, 4000, "KupujemProdajem" });
        }
    }
}
