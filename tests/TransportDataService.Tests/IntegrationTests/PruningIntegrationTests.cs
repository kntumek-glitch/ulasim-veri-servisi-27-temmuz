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
using Microsoft.Extensions.Configuration;

namespace TransportDataService.Tests.IntegrationTests;

public class PruningIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PruningIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase("PruningTestDb");
                });
            });
            
            // Add custom config to test the WaitTimeMinutes logic
            builder.ConfigureAppConfiguration((context, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"JourneyPlan:MaxWaitTimeMinutes", "15"}, // Test value
                    {"JourneyPlan:MaxJourneyTimeMinutes", "120"} // Test value
                });
            });
        });
        
        _client = _factory.CreateClient();
    }

    private async Task SeedDatabaseAsync(AppDbContext context)
    {
        if (await context.GtfsImportRuns.AnyAsync()) return; // already seeded

        int runId = 1001;
        
        var run = new GtfsImportRun
        {
            Id = runId,
            FileHash = "PRUNING_TEST",
            Status = "Completed",
            IsActive = true,
            StartedAt = DateTime.UtcNow
        };
        context.GtfsImportRuns.Add(run);

        context.GtfsAgencies.Add(new GtfsAgency { GtfsImportRunId = runId, AgencyId = "1", AgencyName = "TestAgency", AgencyTimezone = "Europe/Istanbul", AgencyUrl = "http://test.com" });
        context.GtfsCalendars.Add(new GtfsCalendar { GtfsImportRunId = runId, ServiceId = "S1", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2023, 10, 1), EndDate = new DateOnly(2023, 10, 31) });
        
        var route1 = new GtfsRoute { Id = 1001, GtfsImportRunId = runId, RouteId = "R1", RouteShortName = "A-B", RouteType = 3 };
        var route2 = new GtfsRoute { Id = 1002, GtfsImportRunId = runId, RouteId = "R2", RouteShortName = "B-C", RouteType = 3 };
        context.GtfsRoutes.AddRange(route1, route2);
        
        context.GtfsStops.AddRange(
            new GtfsStop { GtfsImportRunId = runId, StopId = "A", StopName = "A", StopLat = 41.01, StopLon = 29.01 },
            new GtfsStop { GtfsImportRunId = runId, StopId = "B", StopName = "B", StopLat = 41.02, StopLon = 29.02 },
            new GtfsStop { GtfsImportRunId = runId, StopId = "C", StopName = "C", StopLat = 41.03, StopLon = 29.03 }
        );

        var trip1 = new GtfsTrip { Id = 1001, GtfsImportRunId = runId, GtfsRouteId = route1.Id, TripId = "T1", ServiceId = "S1", DirectionId = 0 }; // A -> B
        var trip2 = new GtfsTrip { Id = 1002, GtfsImportRunId = runId, GtfsRouteId = route2.Id, TripId = "T2", ServiceId = "S1", DirectionId = 0 }; // B -> C
        context.GtfsTrips.AddRange(trip1, trip2);
        
        // T1 arrives at B at 08:15:00
        context.GtfsStopTimes.AddRange(
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip1.Id, StopId = "A", StopSequence = 1, ArrivalTimeRaw = "08:00:00", DepartureTimeRaw = "08:00:00", ArrivalSeconds = 28800, DepartureSeconds = 28800 },
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip1.Id, StopId = "B", StopSequence = 2, ArrivalTimeRaw = "08:15:00", DepartureTimeRaw = "08:15:00", ArrivalSeconds = 29700, DepartureSeconds = 29700 }
        );
        
        // T2 departs from B at 09:30:00 (75 minutes wait time! MaxWaitTime is 15 in this config)
        context.GtfsStopTimes.AddRange(
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip2.Id, StopId = "B", StopSequence = 1, ArrivalTimeRaw = "09:30:00", DepartureTimeRaw = "09:30:00", ArrivalSeconds = 34200, DepartureSeconds = 34200 },
            new GtfsStopTime { GtfsImportRunId = runId, GtfsTripId = trip2.Id, StopId = "C", StopSequence = 2, ArrivalTimeRaw = "09:45:00", DepartureTimeRaw = "09:45:00", ArrivalSeconds = 35100, DepartureSeconds = 35100 }
        );

        context.GtfsTransfers.Add(new GtfsTransfer { GtfsImportRunId = runId, FromStopId = "B", ToStopId = "B" });
        
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_ShouldApplyWaitTimePruning_AndReturnNoRoute()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedDatabaseAsync(context);
        
        var request = new
        {
            origin = new { lat = 41.01, lon = 29.01 }, // A
            destination = new { lat = 41.03, lon = 29.03 }, // C
            departureDateTime = new DateTime(2023, 10, 05, 08, 00, 00, DateTimeKind.Utc),
            maxWalkingMeters = 1500,
            maxTransfers = 1,
            maxResults = 5
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/journey-plans/search", content);
        
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status Code: {response.StatusCode}. Content: {json}");
        
        using var doc = JsonDocument.Parse(json);
        var reasonCode = doc.RootElement.GetProperty("reasonCode").GetString();
        
        // Since the wait time is 75 minutes, and our configuration allows only 15 minutes, 
        // it should prune this route and return NO_ROUTE_FOUND or SUCCESS with empty itineraries.
        var itineraries = doc.RootElement.GetProperty("itineraries").GetArrayLength();
        Assert.Equal(0, itineraries);
    }
}
