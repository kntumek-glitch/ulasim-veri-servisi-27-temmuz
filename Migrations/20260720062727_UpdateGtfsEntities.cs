using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ulasım_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateGtfsEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
           

            migrationBuilder.CreateTable(
                name: "GtfsAgencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgencyId = table.Column<string>(type: "text", nullable: false),
                    AgencyName = table.Column<string>(type: "text", nullable: false),
                    AgencyUrl = table.Column<string>(type: "text", nullable: false),
                    AgencyTimezone = table.Column<string>(type: "text", nullable: false),
                    AgencyLang = table.Column<string>(type: "text", nullable: true),
                    AgencyPhone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsAgencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GtfsCalendarDates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ExceptionType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsCalendarDates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GtfsCalendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    Monday = table.Column<bool>(type: "boolean", nullable: false),
                    Tuesday = table.Column<bool>(type: "boolean", nullable: false),
                    Wednesday = table.Column<bool>(type: "boolean", nullable: false),
                    Thursday = table.Column<bool>(type: "boolean", nullable: false),
                    Friday = table.Column<bool>(type: "boolean", nullable: false),
                    Saturday = table.Column<bool>(type: "boolean", nullable: false),
                    Sunday = table.Column<bool>(type: "boolean", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GtfsShapePoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShapeId = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsShapePoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GtfsStops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StopId = table.Column<string>(type: "text", nullable: false),
                    StopCode = table.Column<string>(type: "text", nullable: false),
                    StopName = table.Column<string>(type: "text", nullable: false),
                    StopLat = table.Column<double>(type: "double precision", nullable: false),
                    StopLon = table.Column<double>(type: "double precision", nullable: false),
                    StopDesc = table.Column<string>(type: "text", nullable: true),
                    ZoneId = table.Column<string>(type: "text", nullable: true),
                    StopUrl = table.Column<string>(type: "text", nullable: true),
                    LocationType = table.Column<int>(type: "integer", nullable: true),
                    ParentStation = table.Column<string>(type: "text", nullable: true),
                    PlatformCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsStops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GtfsRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RouteId = table.Column<string>(type: "text", nullable: false),
                    AgencyId = table.Column<string>(type: "text", nullable: false),
                    RouteShortName = table.Column<string>(type: "text", nullable: false),
                    RouteLongName = table.Column<string>(type: "text", nullable: false),
                    RouteType = table.Column<int>(type: "integer", nullable: true),
                    RouteColor = table.Column<string>(type: "text", nullable: true),
                    RouteTextColor = table.Column<string>(type: "text", nullable: true),
                    GtfsAgencyId = table.Column<int>(type: "integer", nullable: false),
                    RouteDesc = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GtfsRoutes_GtfsAgencies_GtfsAgencyId",
                        column: x => x.GtfsAgencyId,
                        principalTable: "GtfsAgencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GtfsTrips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TripId = table.Column<string>(type: "text", nullable: false),
                    RouteId = table.Column<string>(type: "text", nullable: false),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    ShapeId = table.Column<string>(type: "text", nullable: true),
                    TripHeadsign = table.Column<string>(type: "text", nullable: true),
                    DirectionId = table.Column<int>(type: "integer", nullable: false),
                    GtfsRouteId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsTrips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GtfsTrips_GtfsRoutes_GtfsRouteId",
                        column: x => x.GtfsRouteId,
                        principalTable: "GtfsRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GtfsStopTimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TripId = table.Column<string>(type: "text", nullable: false),
                    ArrivalTimeRaw = table.Column<string>(type: "text", nullable: false),
                    DepartureTimeRaw = table.Column<string>(type: "text", nullable: false),
                    ArrivalSeconds = table.Column<int>(type: "integer", nullable: true),
                    DepartureSeconds = table.Column<int>(type: "integer", nullable: true),
                    StopId = table.Column<string>(type: "text", nullable: false),
                    StopSequence = table.Column<int>(type: "integer", nullable: false),
                    GtfsTripId = table.Column<int>(type: "integer", nullable: false),
                    GtfsStopId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GtfsStopTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GtfsStopTimes_GtfsStops_GtfsStopId",
                        column: x => x.GtfsStopId,
                        principalTable: "GtfsStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GtfsStopTimes_GtfsTrips_GtfsTripId",
                        column: x => x.GtfsTripId,
                        principalTable: "GtfsTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GtfsAgencies_AgencyId",
                table: "GtfsAgencies",
                column: "AgencyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsRoutes_GtfsAgencyId",
                table: "GtfsRoutes",
                column: "GtfsAgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsRoutes_RouteId",
                table: "GtfsRoutes",
                column: "RouteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStops_StopId",
                table: "GtfsStops",
                column: "StopId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_GtfsStopId",
                table: "GtfsStopTimes",
                column: "GtfsStopId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsStopTimes_GtfsTripId",
                table: "GtfsStopTimes",
                column: "GtfsTripId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_GtfsRouteId",
                table: "GtfsTrips",
                column: "GtfsRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_GtfsTrips_TripId",
                table: "GtfsTrips",
                column: "TripId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GtfsCalendarDates");

            migrationBuilder.DropTable(
                name: "GtfsCalendars");

            migrationBuilder.DropTable(
                name: "GtfsShapePoints");

            migrationBuilder.DropTable(
                name: "GtfsStopTimes");

            migrationBuilder.DropTable(
                name: "GtfsStops");

            migrationBuilder.DropTable(
                name: "GtfsTrips");

            migrationBuilder.DropTable(
                name: "GtfsRoutes");

            migrationBuilder.DropTable(
                name: "GtfsAgencies");

        }
    }
}
