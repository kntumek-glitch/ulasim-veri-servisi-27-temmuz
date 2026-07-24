using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ulasım_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class AddGtfsImportRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GtfsImportRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceUrl = table.Column<string>(type: "text", nullable: false),
                    DownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FeedVersion = table.Column<string>(type: "text", nullable: true),
                    FeedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FeedEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FileHash = table.Column<string>(type: "text", nullable: true),
                    ETag = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<string>(type: "text", nullable: true),
                    AgencyCount = table.Column<int>(type: "integer", nullable: false),
                    RouteCount = table.Column<int>(type: "integer", nullable: false),
                    StopCount = table.Column<int>(type: "integer", nullable: false),
                    TripCount = table.Column<int>(type: "integer", nullable: false),
                    StopTimeCount = table.Column<int>(type: "integer", nullable: false),
                    ShapePointCount = table.Column<int>(type: "integer", nullable: false),
                    FailedRecordCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsImportRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsImportRuns_FileHash",
                table: "GtfsImportRuns",
                column: "FileHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GtfsImportRuns");
        }
    }
}
