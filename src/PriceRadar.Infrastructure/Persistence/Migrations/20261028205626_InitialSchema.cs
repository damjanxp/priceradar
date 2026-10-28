using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PriceRadar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dw");

            migrationBuilder.EnsureSchema(
                name: "match");

            migrationBuilder.EnsureSchema(
                name: "ingest");

            migrationBuilder.CreateTable(
                name: "Book",
                schema: "dw",
                columns: table => new
                {
                    BookId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NormTitle = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    NormAuthor = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false, defaultValue: ""),
                    NormKey = table.Column<string>(type: "varchar(501)", unicode: false, maxLength: 501, nullable: false, computedColumnSql: "[NormAuthor] + '|' + [NormTitle]", stored: true),
                    Origin = table.Column<byte>(type: "tinyint", nullable: false),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Book", x => x.BookId);
                });

            migrationBuilder.CreateTable(
                name: "DimDate",
                schema: "dw",
                columns: table => new
                {
                    DateKey = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Month = table.Column<byte>(type: "tinyint", nullable: false),
                    Day = table.Column<byte>(type: "tinyint", nullable: false),
                    IsoWeek = table.Column<byte>(type: "tinyint", nullable: false),
                    DayOfWeek = table.Column<byte>(type: "tinyint", nullable: false),
                    IsWeekend = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DimDate", x => x.DateKey);
                });

            migrationBuilder.CreateTable(
                name: "Source",
                schema: "ingest",
                columns: table => new
                {
                    SourceId = table.Column<byte>(type: "tinyint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    BaseUrl = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MinDelayMs = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Source", x => x.SourceId);
                });

            migrationBuilder.CreateTable(
                name: "MatchCache",
                schema: "match",
                columns: table => new
                {
                    MatchCacheId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    PromptVersion = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    NormTitle = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    NormAuthor = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    CandidateBookIds = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Decision = table.Column<byte>(type: "tinyint", nullable: false),
                    ResolvedBookId = table.Column<int>(type: "int", nullable: true),
                    Confidence = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastHitAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    HitCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCache", x => x.MatchCacheId);
                    table.ForeignKey(
                        name: "FK_MatchCache_Book_ResolvedBookId",
                        column: x => x.ResolvedBookId,
                        principalSchema: "dw",
                        principalTable: "Book",
                        principalColumn: "BookId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScrapeRun",
                schema: "ingest",
                columns: table => new
                {
                    ScrapeRunId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<byte>(type: "tinyint", nullable: false),
                    ScopeKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Query = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Stage = table.Column<byte>(type: "tinyint", nullable: false),
                    IsComplete = table.Column<bool>(type: "bit", nullable: false),
                    MaxPages = table.Column<short>(type: "smallint", nullable: false),
                    PagesFetched = table.Column<short>(type: "smallint", nullable: false),
                    ListingsFetched = table.Column<int>(type: "int", nullable: false),
                    ListingsRejected = table.Column<int>(type: "int", nullable: false),
                    ListingsLoaded = table.Column<int>(type: "int", nullable: false),
                    AiCalls = table.Column<int>(type: "int", nullable: false),
                    EurRsdRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HangfireJobId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeRun", x => x.ScrapeRunId);
                    table.ForeignKey(
                        name: "FK_ScrapeRun_Source_SourceId",
                        column: x => x.SourceId,
                        principalSchema: "ingest",
                        principalTable: "Source",
                        principalColumn: "SourceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Listing",
                schema: "dw",
                columns: table => new
                {
                    ListingId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: true),
                    MatchStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    MatchScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Url = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    ScopeKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CurrentPriceRsd = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastSeenRunId = table.Column<long>(type: "bigint", nullable: false),
                    MissedRunCount = table.Column<byte>(type: "tinyint", nullable: false),
                    DisappearedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listing", x => x.ListingId);
                    table.ForeignKey(
                        name: "FK_Listing_Book_BookId",
                        column: x => x.BookId,
                        principalSchema: "dw",
                        principalTable: "Book",
                        principalColumn: "BookId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Listing_ScrapeRun_LastSeenRunId",
                        column: x => x.LastSeenRunId,
                        principalSchema: "ingest",
                        principalTable: "ScrapeRun",
                        principalColumn: "ScrapeRunId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Listing_Source_SourceId",
                        column: x => x.SourceId,
                        principalSchema: "ingest",
                        principalTable: "Source",
                        principalColumn: "SourceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StagingListing",
                schema: "ingest",
                columns: table => new
                {
                    StagingListingId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScrapeRunId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RawTitle = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RawAuthor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RawPrice = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RawCondition = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Url = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ScrapedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    PriceRsd = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    NormTitle = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: true),
                    NormAuthor = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    BookId = table.Column<int>(type: "int", nullable: true),
                    MatchStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    MatchScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    ProcessStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    RejectReason = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingListing", x => x.StagingListingId);
                    table.ForeignKey(
                        name: "FK_StagingListing_ScrapeRun_ScrapeRunId",
                        column: x => x.ScrapeRunId,
                        principalSchema: "ingest",
                        principalTable: "ScrapeRun",
                        principalColumn: "ScrapeRunId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceSnapshot",
                schema: "dw",
                columns: table => new
                {
                    PriceSnapshotId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListingId = table.Column<long>(type: "bigint", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: true),
                    SourceId = table.Column<byte>(type: "tinyint", nullable: false),
                    ScrapeRunId = table.Column<long>(type: "bigint", nullable: false),
                    DateKey = table.Column<int>(type: "int", nullable: false),
                    PriceRsd = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSnapshot", x => x.PriceSnapshotId)
                        .Annotation("SqlServer:Clustered", true);
                    table.ForeignKey(
                        name: "FK_PriceSnapshot_Book_BookId",
                        column: x => x.BookId,
                        principalSchema: "dw",
                        principalTable: "Book",
                        principalColumn: "BookId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceSnapshot_DimDate_DateKey",
                        column: x => x.DateKey,
                        principalSchema: "dw",
                        principalTable: "DimDate",
                        principalColumn: "DateKey",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceSnapshot_Listing_ListingId",
                        column: x => x.ListingId,
                        principalSchema: "dw",
                        principalTable: "Listing",
                        principalColumn: "ListingId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceSnapshot_ScrapeRun_ScrapeRunId",
                        column: x => x.ScrapeRunId,
                        principalSchema: "ingest",
                        principalTable: "ScrapeRun",
                        principalColumn: "ScrapeRunId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceSnapshot_Source_SourceId",
                        column: x => x.SourceId,
                        principalSchema: "ingest",
                        principalTable: "Source",
                        principalColumn: "SourceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "ingest",
                table: "Source",
                columns: new[] { "SourceId", "BaseUrl", "Code", "CreatedAtUtc", "IsEnabled", "Kind", "MinDelayMs", "Name" },
                values: new object[,]
                {
                    { (byte)1, null, "fixture", new DateTime(2026, 10, 28, 0, 0, 0, 0, DateTimeKind.Utc), true, (byte)0, 0, "Fixture files" },
                    { (byte)2, null, "synthetic", new DateTime(2026, 10, 28, 0, 0, 0, 0, DateTimeKind.Utc), true, (byte)1, 0, "Synthetic generator" },
                    { (byte)3, "https://www.kupindo.com", "kupindo", new DateTime(2026, 10, 28, 0, 0, 0, 0, DateTimeKind.Utc), false, (byte)2, 4000, "Kupindo" },
                    { (byte)4, "https://www.kupujemprodajem.com", "kp", new DateTime(2026, 10, 28, 0, 0, 0, 0, DateTimeKind.Utc), false, (byte)2, 4000, "KupujemProdajem" }
                });

            migrationBuilder.CreateIndex(
                name: "UX_Book_NormKey",
                schema: "dw",
                table: "Book",
                column: "NormKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DimDate_Date",
                schema: "dw",
                table: "DimDate",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Listing_Book_Status",
                schema: "dw",
                table: "Listing",
                columns: new[] { "BookId", "Status" })
                .Annotation("SqlServer:Include", new[] { "CurrentPriceRsd", "LastSeenAtUtc", "DisappearedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Listing_Scope_Status",
                schema: "dw",
                table: "Listing",
                columns: new[] { "SourceId", "ScopeKey", "Status" })
                .Annotation("SqlServer:Include", new[] { "LastSeenRunId", "MissedRunCount" });

            migrationBuilder.CreateIndex(
                name: "UX_Listing_Source_External",
                schema: "dw",
                table: "Listing",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MatchCache_InputHash",
                schema: "match",
                table: "MatchCache",
                column: "InputHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PriceSnapshot_Listing_Run",
                schema: "dw",
                table: "PriceSnapshot",
                columns: new[] { "ListingId", "ScrapeRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRun_Source_Created",
                schema: "ingest",
                table: "ScrapeRun",
                columns: new[] { "SourceId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_ScrapeRun_ActivePerSource",
                schema: "ingest",
                table: "ScrapeRun",
                column: "SourceId",
                unique: true,
                filter: "[Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "UX_ScrapeRun_IdempotencyKey",
                schema: "ingest",
                table: "ScrapeRun",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Source_Code",
                schema: "ingest",
                table: "Source",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staging_Run_Status",
                schema: "ingest",
                table: "StagingListing",
                columns: new[] { "ScrapeRunId", "ProcessStatus" });

            migrationBuilder.CreateIndex(
                name: "UX_Staging_Run_External",
                schema: "ingest",
                table: "StagingListing",
                columns: new[] { "ScrapeRunId", "ExternalId" },
                unique: true);

            // Hand-added: populate the date dimension 2024-01-01 .. 2030-12-31 (spec §5.2).
            migrationBuilder.Sql(PriceRadar.Infrastructure.Sql.SqlResource.Load("Seed/dw.DimDate.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchCache",
                schema: "match");

            migrationBuilder.DropTable(
                name: "PriceSnapshot",
                schema: "dw");

            migrationBuilder.DropTable(
                name: "StagingListing",
                schema: "ingest");

            migrationBuilder.DropTable(
                name: "DimDate",
                schema: "dw");

            migrationBuilder.DropTable(
                name: "Listing",
                schema: "dw");

            migrationBuilder.DropTable(
                name: "Book",
                schema: "dw");

            migrationBuilder.DropTable(
                name: "ScrapeRun",
                schema: "ingest");

            migrationBuilder.DropTable(
                name: "Source",
                schema: "ingest");
        }
    }
}
