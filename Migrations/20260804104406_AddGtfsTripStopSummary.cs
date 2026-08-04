using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class AddGtfsTripStopSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GtfsTripStopSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GtfsImportRunId = table.Column<int>(type: "integer", nullable: false),
                    GtfsTripId = table.Column<int>(type: "integer", nullable: false),
                    StopSequences = table.Column<List<int>>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsTripStopSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GtfsTripStopSummaries_GtfsImportRuns_GtfsImportRunId",
                        column: x => x.GtfsImportRunId,
                        principalTable: "GtfsImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GtfsTripStopSummaries_GtfsTrips_GtfsTripId",
                        column: x => x.GtfsTripId,
                        principalTable: "GtfsTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTripStopSummaries_GtfsImportRunId_GtfsTripId",
                table: "GtfsTripStopSummaries",
                columns: new[] { "GtfsImportRunId", "GtfsTripId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTripStopSummaries_GtfsTripId",
                table: "GtfsTripStopSummaries",
                column: "GtfsTripId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GtfsTripStopSummaries");
        }
    }
}
