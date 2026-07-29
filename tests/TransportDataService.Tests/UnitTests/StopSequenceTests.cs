using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Controllers;
using ulasım_veri_servisi.Models.Gtfs;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

[Collection("PostgreSql collection")]
public class StopSequenceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _context = null!;

    public StopSequenceTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();
        await TruncateGtfsTablesAsync();
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        return Task.CompletedTask;
    }

    private async Task TruncateGtfsTablesAsync()
    {
        await _context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "GtfsStopTimes", "GtfsShapePoints", "GtfsCalendars",
                "GtfsCalendarDates", "GtfsTrips", "GtfsStops", "GtfsRoutes",
                "GtfsAgencies", "GtfsImportRuns" RESTART IDENTITY CASCADE
            """);
    }

    [Fact]
    public async Task GetRouteStops_ReturnsStopsOrderedBySequence()
    {
        // Arrange - Seed data
        var run = new GtfsImportRun { Id = 1, Status = "Completed", IsActive = true, FinishedAt = DateTime.UtcNow };
        
        var route = new GtfsRoute { GtfsImportRunId = 1, Id = 1, RouteId = "route_1", RouteShortName = "R1" };
        var trip = new GtfsTrip { GtfsImportRunId = 1, Id = 1, TripId = "trip_1", RouteId = "route_1", DirectionId = 0, GtfsRouteId = 1 };
        
        var stop1 = new GtfsStop { GtfsImportRunId = 1, Id = 1, StopId = "stop_1", StopName = "Stop 1" };
        var stop2 = new GtfsStop { GtfsImportRunId = 1, Id = 2, StopId = "stop_2", StopName = "Stop 2" };
        var stop3 = new GtfsStop { GtfsImportRunId = 1, Id = 3, StopId = "stop_3", StopName = "Stop 3" };

        // Add stop times OUT OF ORDER
        var st3 = new GtfsStopTime { GtfsImportRunId = 1, Id = 1, GtfsTripId = 1, GtfsStopId = 3, StopSequence = 30 };
        var st1 = new GtfsStopTime { GtfsImportRunId = 1, Id = 2, GtfsTripId = 1, GtfsStopId = 1, StopSequence = 10 };
        var st2 = new GtfsStopTime { GtfsImportRunId = 1, Id = 3, GtfsTripId = 1, GtfsStopId = 2, StopSequence = 20 };

        _context.GtfsImportRuns.Add(run);
        _context.GtfsRoutes.Add(route);
        _context.GtfsTrips.Add(trip);
        _context.GtfsStops.AddRange(stop1, stop2, stop3);
        _context.GtfsStopTimes.AddRange(st3, st1, st2);
        await _context.SaveChangesAsync();

        var controller = new GtfsController(_context);

        // Act
        var result = await controller.GetRouteStops("route_1", 0);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var stops = okResult.Value.Should().BeAssignableTo<IEnumerable<RouteStopDto>>().Subject.ToList();

        stops.Should().HaveCount(3);
        // Ensure they are ordered by sequence
        stops[0].StopSequence.Should().Be(10);
        stops[0].StopId.Should().Be("stop_1");

        stops[1].StopSequence.Should().Be(20);
        stops[1].StopId.Should().Be("stop_2");

        stops[2].StopSequence.Should().Be(30);
        stops[2].StopId.Should().Be("stop_3");
    }
}
