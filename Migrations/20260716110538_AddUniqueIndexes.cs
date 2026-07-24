using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasım_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StopRoutes_StopId",
                table: "StopRoutes");

            migrationBuilder.CreateIndex(
                name: "IX_Stops_ExternalStopId",
                table: "Stops",
                column: "ExternalStopId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StopRoutes_StopId_RouteNumber",
                table: "StopRoutes",
                columns: new[] { "StopId", "RouteNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stops_ExternalStopId",
                table: "Stops");

            migrationBuilder.DropIndex(
                name: "IX_StopRoutes_StopId_RouteNumber",
                table: "StopRoutes");

            migrationBuilder.CreateIndex(
                name: "IX_StopRoutes_StopId",
                table: "StopRoutes",
                column: "StopId");
        }
    }
}
