using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAgencyIdFromGtfsRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GtfsTrips_GtfsRoutes_RouteId1",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsTrips_RouteId1",
                table: "GtfsTrips");

            migrationBuilder.DropColumn(
                name: "RouteId1",
                table: "GtfsTrips");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "GtfsRoutes");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_GtfsRouteId",
                table: "GtfsTrips",
                column: "GtfsRouteId");

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsTrips_GtfsRoutes_GtfsRouteId",
                table: "GtfsTrips",
                column: "GtfsRouteId",
                principalTable: "GtfsRoutes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GtfsTrips_GtfsRoutes_GtfsRouteId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsTrips_GtfsRouteId",
                table: "GtfsTrips");

            migrationBuilder.AddColumn<int>(
                name: "RouteId1",
                table: "GtfsTrips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AgencyId",
                table: "GtfsRoutes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_RouteId1",
                table: "GtfsTrips",
                column: "RouteId1");

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsTrips_GtfsRoutes_RouteId1",
                table: "GtfsTrips",
                column: "RouteId1",
                principalTable: "GtfsRoutes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}


