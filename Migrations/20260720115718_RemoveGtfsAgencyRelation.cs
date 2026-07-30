using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGtfsAgencyRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GtfsRoutes_GtfsAgencies_GtfsAgencyId",
                table: "GtfsRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_GtfsTrips_GtfsRoutes_GtfsRouteId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsTrips_GtfsRouteId",
                table: "GtfsTrips");

            migrationBuilder.DropIndex(
                name: "IX_GtfsRoutes_GtfsAgencyId",
                table: "GtfsRoutes");

            migrationBuilder.DropColumn(
                name: "GtfsAgencyId",
                table: "GtfsRoutes");

            migrationBuilder.AlterColumn<int>(
                name: "DirectionId",
                table: "GtfsTrips",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "RouteId1",
                table: "GtfsTrips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AlterColumn<int>(
                name: "DirectionId",
                table: "GtfsTrips",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GtfsAgencyId",
                table: "GtfsRoutes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_GtfsRouteId",
                table: "GtfsTrips",
                column: "GtfsRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsRoutes_GtfsAgencyId",
                table: "GtfsRoutes",
                column: "GtfsAgencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsRoutes_GtfsAgencies_GtfsAgencyId",
                table: "GtfsRoutes",
                column: "GtfsAgencyId",
                principalTable: "GtfsAgencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GtfsTrips_GtfsRoutes_GtfsRouteId",
                table: "GtfsTrips",
                column: "GtfsRouteId",
                principalTable: "GtfsRoutes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}


