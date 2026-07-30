using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasım_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class JourneyPlanningPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GtfsStopTimes_GtfsTripId",
                table: "GtfsStopTimes");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_GtfsTripId_StopSequence",
                table: "GtfsStopTimes",
                columns: new[] { "GtfsTripId", "StopSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_StopId_DepartureSeconds",
                table: "GtfsStopTimes",
                columns: new[] { "StopId", "DepartureSeconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GtfsStopTimes_GtfsTripId_StopSequence",
                table: "GtfsStopTimes");

            migrationBuilder.DropIndex(
                name: "IX_GtfsStopTimes_StopId_DepartureSeconds",
                table: "GtfsStopTimes");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_GtfsTripId",
                table: "GtfsStopTimes",
                column: "GtfsTripId");
        }
    }
}
