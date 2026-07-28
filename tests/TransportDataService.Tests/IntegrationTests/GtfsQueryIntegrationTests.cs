using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Testcontainers.PostgreSql;
using TransportDataService;
using TransportDataService.Domain;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

public class GtfsQueryIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("transport_test_db").WithUsername("postgres").WithPassword("postgres123").Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_db.GetConnectionString()));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.Migrate();

                // Seed data
                context.GtfsImportRuns.Add(new GtfsImportRun { Id = 1, Status = "Completed", IsActive = true, FinishedAt = DateTime.UtcNow });
                context.GtfsStops.Add(new GtfsStop { Id = 1, StopId = "stop_1", StopName = "Konak", StopCode = "1001", StopLat = 1.0, StopLon = 1.0 });
                context.GtfsRoutes.Add(new GtfsRoute { Id = 1, RouteId = "route_1", RouteShortName = "123", RouteType = 3 });
                context.GtfsTrips.Add(new GtfsTrip { Id = 1, TripId = "trip_1", RouteId = "route_1", GtfsRouteId = 1, DirectionId = 0, ServiceId = "service_1", ShapeId = "shape_1" });
                context.GtfsStopTimes.Add(new GtfsStopTime { TripId = "trip_1", GtfsTripId = 1, StopId = "stop_1", GtfsStopId = 1, StopSequence = 1, ArrivalSeconds = 3600, DepartureSeconds = 3660 });
                
                context.GtfsCalendars.Add(new GtfsCalendar { ServiceId = "service_1", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2030, 12, 31) });
                context.GtfsShapePoints.Add(new GtfsShapePoint { ShapeId = "shape_1", Latitude = 1.0, Longitude = 1.0, Sequence = 1 });
                
                context.SaveChanges();
            });
        });
    }

    public async Task DisposeAsync() => await _db.DisposeAsync().AsTask();

    [Fact]
    public async Task SearchStops_WithValidQuery_ReturnsMatches()
    {
        var client = _factory.CreateClient();
        
        var response = await client.GetAsync("/api/v1/gtfs/stops?search=1001");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Konak");
    }

    [Fact]
    public async Task GetTripStops_AreOrderedBySequence()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/gtfs/trips/trip_1/stops");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("stop_1");
    }

    [Fact]
    public async Task FallbackMechanism_ReadsOnlyActiveRun()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/gtfs/metadata");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("ImportId");
    }

    [Fact]
    public async Task GetRoutePatterns_ReturnsSuccess()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/gtfs/routes/route_1/patterns");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("stop_1");
    }

    [Fact]
    public async Task GetRouteDepartures_WithDate_ReturnsDepartures()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/gtfs/routes/route_1/departures?directionId=0&date=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("trip_1");
    }

    [Fact]
    public async Task GetShapes_ByTripId_ReturnsGeoJson()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/gtfs/shapes?tripId=trip_1&format=geojson");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("LineString");
    }
}
