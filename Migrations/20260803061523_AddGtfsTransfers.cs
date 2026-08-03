using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class AddGtfsTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GtfsTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GtfsImportRunId = table.Column<int>(type: "integer", nullable: false),
                    FromStopId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ToStopId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: false),
                    WalkingTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsSamePhysicalStop = table.Column<bool>(type: "boolean", nullable: false),
                    IsSameParentStation = table.Column<bool>(type: "boolean", nullable: false),
                    IsSameCoordinateCluster = table.Column<bool>(type: "boolean", nullable: false),
                    CalculationMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GtfsTransfers_GtfsImportRuns_GtfsImportRunId",
                        column: x => x.GtfsImportRunId,
                        principalTable: "GtfsImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTransfers_GtfsImportRunId_FromStopId_DistanceMeters",
                table: "GtfsTransfers",
                columns: new[] { "GtfsImportRunId", "FromStopId", "DistanceMeters" });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTransfers_GtfsImportRunId_FromStopId_ToStopId",
                table: "GtfsTransfers",
                columns: new[] { "GtfsImportRunId", "FromStopId", "ToStopId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GtfsTransfers");
        }
    }
}
