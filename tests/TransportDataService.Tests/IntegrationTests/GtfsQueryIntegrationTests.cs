using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class GtfsQueryIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GtfsQueryIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        // Truncate before seeding to prevent PK conflicts from other tests sharing DB
        context.Database.ExecuteSqlRaw(@"TRUNCATE TABLE ""GtfsImportRuns"", ""GtfsStops"", ""GtfsRoutes"", ""GtfsTrips"", ""GtfsStopTimes"", ""GtfsCalendars"", ""GtfsShapePoints"", ""GtfsTransfers"" RESTART IDENTITY CASCADE");

        // Seed data without explicit Ids so Postgres sequence advances normally
        var run = new GtfsImportRun { Status = "Completed", IsActive = true, FinishedAt = DateTime.UtcNow, FileHash = "ACTIVE_HASH" };
        var stop = new GtfsStop { GtfsImportRun = run, StopId = "stop_1", StopName = "Konak", StopCode = "1001", StopLat = 1.0, StopLon = 1.0 };
        var route = new GtfsRoute { GtfsImportRun = run, RouteId = "route_1", RouteShortName = "123", RouteType = 3 };
        var trip = new GtfsTrip { GtfsImportRun = run, TripId = "trip_1", Route = route, RouteId = "route_1", DirectionId = 0, ServiceId = "service_1", ShapeId = "shape_1" };
        var stopTime = new GtfsStopTime { GtfsImportRun = run, TripId = "trip_1", Trip = trip, StopId = "stop_1", Stop = stop, StopSequence = 1, ArrivalSeconds = 3600, DepartureSeconds = 3660 };
        var calendar = new GtfsCalendar { GtfsImportRun = run, ServiceId = "service_1", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2030, 12, 31) };
        var shapePoint = new GtfsShapePoint { GtfsImportRun = run, ShapeId = "shape_1", Latitude = 1.0, Longitude = 1.0, Sequence = 1 };

        context.GtfsImportRuns.Add(run);
        context.GtfsStops.Add(stop);
        context.GtfsRoutes.Add(route);
        context.GtfsTrips.Add(trip);
        context.GtfsStopTimes.Add(stopTime);
        context.GtfsCalendars.Add(calendar);
        context.GtfsShapePoints.Add(shapePoint);
        
        context.SaveChanges();
    }

    [Fact]
    public async Task SearchStops_WithValidQuery_ReturnsMatches()
    {
        var response = await _client.GetAsync("/api/v1/gtfs/stops?search=1001");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Konak");
    }

    [Fact]
    public async Task GetTripStops_AreOrderedBySequence()
    {
        var response = await _client.GetAsync("/api/v1/gtfs/trips/trip_1/stops");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("stop_1");
    }

    [Fact]
    public async Task FallbackMechanism_ReadsOnlyActiveRun()
    {
        var response = await _client.GetAsync("/api/v1/gtfs/metadata");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"importId\"");
        content.Should().Contain("\"fileHash\":\"ACTIVE_HASH\"");
    }

    [Fact]
    public async Task GetRoutePatterns_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/v1/gtfs/routes/route_1/patterns");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("stop_1");
    }

    [Fact]
    public async Task GetRouteDepartures_WithDate_ReturnsDepartures()
    {
        var response = await _client.GetAsync("/api/v1/gtfs/routes/route_1/departures?directionId=0&date=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("trip_1");
    }

    [Fact]
    public async Task GetShapes_ByTripId_ReturnsGeoJson()
    {
        var response = await _client.GetAsync("/api/v1/gtfs/shapes?tripId=trip_1&format=geojson");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("LineString");
    }
}
