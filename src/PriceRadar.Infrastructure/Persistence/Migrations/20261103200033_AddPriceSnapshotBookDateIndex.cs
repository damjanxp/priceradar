using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceRadar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceSnapshotBookDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PriceSnapshot_Book_Date",
                schema: "dw",
                table: "PriceSnapshot",
                columns: new[] { "BookId", "DateKey" })
                .Annotation("SqlServer:Include", new[] { "ListingId", "PriceRsd", "CapturedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PriceSnapshot_Book_Date",
                schema: "dw",
                table: "PriceSnapshot");
        }
    }
}
