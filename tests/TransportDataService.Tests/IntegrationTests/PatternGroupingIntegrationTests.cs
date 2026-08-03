using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using Xunit;
using System.Text.Json;
using System.Net.Http;
using System.Text;

namespace TransportDataService.Tests.IntegrationTests;

public class PatternGroupingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PatternGroupingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase("PatternTestDb");
                });
            });
        });
        
        _client = _factory.CreateClient();
    }

    private async Task SeedDatabaseAsync(AppDbContext context)
    {
        if (await context.GtfsImportRuns.AnyAsync()) return;

        int runId = 2001;
        
        var run = new GtfsImportRun
        {
            Id = runId,
            FileHash = "PATTERN_TEST",
            Status = "Completed",
            IsActive = true,
            StartedAt = DateTime.UtcNow
        };
        context.GtfsImportRuns.Add(run);

        context.GtfsAgencies.Add(new GtfsAgency { GtfsImportRunId = runId, AgencyId = "1", AgencyName = "TestAgency", AgencyTimezone = "Europe/Istanbul", AgencyUrl = "http://test.com" });
        context.GtfsCalendars.Add(new GtfsCalendar { GtfsImportRunId = runId, ServiceId = "S1", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2023, 10, 1), EndDate = new DateOnly(2023, 10, 31) });
        
        var route1 = new GtfsRoute { Id = 2001, GtfsImportRunId = runId, RouteId = "R1", RouteShortName = "A-B", RouteType = 3 };
        context.GtfsRoutes.Add(route1);
        
        context.GtfsStops.AddRange(
            new GtfsStop { GtfsImportRunId = runId, StopId = "A", StopName = "A", StopLat = 41.01, StopLon = 29.01 },
            new GtfsStop { GtfsImportRunId = runId, StopId = "B", StopName = "B", StopLat = 41.02, StopLon = 29.02 }
        );

        // Trip 1 and Trip 2 have the EXACT SAME ShapeId and RouteId (Same Pattern)
        var trip1 = new GtfsTrip { Id = 2001, GtfsImportRunId = runId, GtfsRouteId = route1.Id, TripId = "T1", ServiceId = "S1", DirectionId = 0, ShapeId = "SHAPE_1" }; 
        var trip2 = new GtfsTrip { Id = 2002, GtfsImportRunId = runId, GtfsRouteId = route1.Id, TripId = "T2", ServiceId = "S1", DirectionId = 0, ShapeId = "SHAPE_1" }; 
        context.GtfsTrips.AddRange(trip1, trip2);
        
        // Trip 1 Departs at 08:00
        context.GtfsStopTimes.AddRange(
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip1.Id, StopId = "A", StopSequence = 1, ArrivalTimeRaw = "08:00:00", DepartureTimeRaw = "08:00:00", ArrivalSeconds = 28800, DepartureSeconds = 28800 },
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip1.Id, StopId = "B", StopSequence = 2, ArrivalTimeRaw = "08:15:00", DepartureTimeRaw = "08:15:00", ArrivalSeconds = 29700, DepartureSeconds = 29700 }
        );
        
        // Trip 2 Departs at 08:30 (30 mins later)
        context.GtfsStopTimes.AddRange(
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip2.Id, StopId = "A", StopSequence = 1, ArrivalTimeRaw = "08:30:00", DepartureTimeRaw = "08:30:00", ArrivalSeconds = 30600, DepartureSeconds = 30600 },
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip2.Id, StopId = "B", StopSequence = 2, ArrivalTimeRaw = "08:45:00", DepartureTimeRaw = "08:45:00", ArrivalSeconds = 31500, DepartureSeconds = 31500 }
        );

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_ShouldGroupTripsByPatternId_AndPreserveTimeIntegrity()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedDatabaseAsync(context);
        
        var request = new 
        {
            origin = new { lat = 41.01, lon = 29.01 }, // A
            destination = new { lat = 41.02, lon = 29.02 }, // B
            departureDateTime = new DateTimeOffset(2023, 10, 05, 07, 30, 00, TimeSpan.FromHours(3)),
            maxWalkingMeters = 1500,
            maxTransfers = 0,
            maxResults = 5 // Request up to 5 results
        };

        // Act
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/journey-plans/search", content);
        
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status Code: {response.StatusCode}. Content: {json}");
        
        using var doc = JsonDocument.Parse(json);
        var reasonCode = doc.RootElement.GetProperty("reasonCode").GetString();
        Assert.Equal("SUCCESS", reasonCode);
        
        var itineraries = doc.RootElement.GetProperty("itineraries");
        var itineraryCount = itineraries.GetArrayLength();
        
        // Assert: Grouping Verification
        // Even though we have 2 trips (T1 and T2) valid for this O-D pair and MaxResults is 5, 
        // they belong to the same pattern (SHAPE_1). Thus, they should be grouped into a single itinerary!
        Assert.Equal(1, itineraryCount);
        
        var firstItinerary = itineraries[0];
        var legs = firstItinerary.GetProperty("legs");
        var transitLeg = legs.EnumerateArray().FirstOrDefault(x => x.GetProperty("mode").GetString() == "TRANSIT");
        
        Assert.True(transitLeg.ValueKind != JsonValueKind.Undefined);
        
        // Assert: Data Integrity Verification
        var patternId = transitLeg.GetProperty("patternId").GetString();
        var shapeId = transitLeg.GetProperty("shapeId").GetString();
        var tripId = transitLeg.GetProperty("tripId").GetString();
        var routeId = transitLeg.GetProperty("routeId").GetString();
        var directionId = transitLeg.GetProperty("directionId").GetInt32();
        var serviceId = transitLeg.GetProperty("serviceId").GetString();
        var serviceDate = transitLeg.GetProperty("serviceDate").GetString();
        
        Assert.Equal("P_SHAPE_1", patternId);
        Assert.Equal("SHAPE_1", shapeId);
        Assert.Equal("T1", tripId); // Should pick the earliest one
        Assert.Equal("R1", routeId);
        Assert.Equal(0, directionId);
        Assert.Equal("S1", serviceId);
        Assert.Equal("2023-10-05", serviceDate); // Our requested date
    }
}
