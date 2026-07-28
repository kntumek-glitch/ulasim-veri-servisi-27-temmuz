using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasım_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class VersionedFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GtfsTrips_TripId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsStops_StopId",
                table: "GtfsStops");

            migrationBuilder.DropIndex(
                name: "IX_GtfsRoutes_RouteId",
                table: "GtfsRoutes");

            migrationBuilder.DropIndex(
                name: "IX_GtfsAgencies_AgencyId",
                table: "GtfsAgencies");

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsTrips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsStopTimes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsStops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsShapePoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsRoutes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsCalendars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsCalendarDates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GtfsImportRunId",
                table: "GtfsAgencies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_GtfsImportRunId",
                table: "GtfsTrips",
                column: "GtfsImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_TripId_GtfsImportRunId",
                table: "GtfsTrips",
                columns: new[] { "TripId", "GtfsImportRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_GtfsImportRunId",
                table: "GtfsStopTimes",
                column: "GtfsImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStops_GtfsImportRunId",
                table: "GtfsStops",
                column: "GtfsImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStops_StopId_GtfsImportRunId",
                table: "GtfsStops",
                columns: new[] { "StopId", "GtfsImportRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsShapePoints_GtfsImportRunId",
                table: "GtfsShapePoints",
                column: "GtfsImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsRoutes_GtfsImportRunId",
                table: "GtfsRoutes",
                column: "GtfsImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsRoutes_RouteId_GtfsImportRunId",
                table: "GtfsRoutes",
                columns: new[] { "RouteId", "GtfsImportRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsCalendars_GtfsImportRunId",
                table: "GtfsCalendars",
                column: "GtfsImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsCalendarDates_GtfsImportRunId",
                table: "GtfsCalendarDates",
                column: "GtfsImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsAgencies_AgencyId_GtfsImportRunId",
                table: "GtfsAgencies",
                columns: new[] { "AgencyId", "GtfsImportRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsAgencies_GtfsImportRunId",
                table: "GtfsAgencies",
                column: "GtfsImportRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsAgencies_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsAgencies",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsCalendarDates_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsCalendarDates",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsCalendars_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsCalendars",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsRoutes_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsRoutes",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsShapePoints_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsShapePoints",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsStops_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsStops",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsStopTimes_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsStopTimes",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsTrips_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsTrips",
                column: "GtfsImportRunId",
                principalTable: "GtfsImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GtfsAgencies_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsAgencies");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsCalendarDates_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsCalendarDates");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsCalendars_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsCalendars");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsRoutes_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsShapePoints_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsShapePoints");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsStops_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsStops");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsStopTimes_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsStopTimes");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsTrips_GtfsImportRuns_GtfsImportRunId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsTrips_GtfsImportRunId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsTrips_TripId_GtfsImportRunId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsStopTimes_GtfsImportRunId",
                table: "GtfsStopTimes");

            migrationBuilder.DropIndex(
                name: "IX_GtfsStops_GtfsImportRunId",
                table: "GtfsStops");

            migrationBuilder.DropIndex(
                name: "IX_GtfsStops_StopId_GtfsImportRunId",
                table: "GtfsStops");

            migrationBuilder.DropIndex(
                name: "IX_GtfsShapePoints_GtfsImportRunId",
                table: "GtfsShapePoints");

            migrationBuilder.DropIndex(
                name: "IX_GtfsRoutes_GtfsImportRunId",
                table: "GtfsRoutes");

            migrationBuilder.DropIndex(
                name: "IX_GtfsRoutes_RouteId_GtfsImportRunId",
                table: "GtfsRoutes");

            migrationBuilder.DropIndex(
                name: "IX_GtfsCalendars_GtfsImportRunId",
                table: "GtfsCalendars");

            migrationBuilder.DropIndex(
                name: "IX_GtfsCalendarDates_GtfsImportRunId",
                table: "GtfsCalendarDates");

            migrationBuilder.DropIndex(
                name: "IX_GtfsAgencies_AgencyId_GtfsImportRunId",
                table: "GtfsAgencies");

            migrationBuilder.DropIndex(
                name: "IX_GtfsAgencies_GtfsImportRunId",
                table: "GtfsAgencies");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsTrips");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsStopTimes");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsStops");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsShapePoints");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsRoutes");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsCalendars");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsCalendarDates");

            migrationBuilder.DropColumn(
                name: "GtfsImportRunId",
                table: "GtfsAgencies");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_TripId",
                table: "GtfsTrips",
                column: "TripId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStops_StopId",
                table: "GtfsStops",
                column: "StopId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsRoutes_RouteId",
                table: "GtfsRoutes",
                column: "RouteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsAgencies_AgencyId",
                table: "GtfsAgencies",
                column: "AgencyId",
                unique: true);
        }
    }
}
