using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class AddGtfsRouteFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgencyId",
                table: "GtfsRoutes",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "GtfsRoutes");
        }
    }
}

