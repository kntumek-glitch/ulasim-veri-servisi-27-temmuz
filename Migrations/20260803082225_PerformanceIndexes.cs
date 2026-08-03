using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GtfsStopTimes_StopId_DepartureSeconds",
                table: "GtfsStopTimes");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_ServiceId",
                table: "GtfsTrips",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_StopId_DepartureSeconds_GtfsTripId",
                table: "GtfsStopTimes",
                columns: new[] { "StopId", "DepartureSeconds", "GtfsTripId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GtfsTrips_ServiceId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsStopTimes_StopId_DepartureSeconds_GtfsTripId",
                table: "GtfsStopTimes");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_StopId_DepartureSeconds",
                table: "GtfsStopTimes",
                columns: new[] { "StopId", "DepartureSeconds" });
        }
    }
}
