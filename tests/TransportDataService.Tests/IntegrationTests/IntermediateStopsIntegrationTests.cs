using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using Xunit;
using Xunit.Abstractions;

namespace TransportDataService.Tests.IntegrationTests;

public class IntermediateStopsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public IntermediateStopsIntegrationTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private async Task SeedDatabaseAsync(int runId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Disable existing active runs
        var activeRuns = context.GtfsImportRuns.Where(r => r.IsActive);
        foreach (var r in activeRuns) r.IsActive = false;

        var run = new GtfsImportRun
        {
            Id = runId,
            FileHash = $"test-hash-{runId}",
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Status = "Completed",
            IsActive = true
        };
        context.GtfsImportRuns.Add(run);

        var agency = new GtfsAgency { GtfsImportRunId = runId, AgencyId = "AG1", AgencyName = "Test Agency", AgencyTimezone = "Europe/Istanbul" };
        context.GtfsAgencies.Add(agency);

        var calendar = new GtfsCalendar
        {
            GtfsImportRunId = runId,
            ServiceId = "SRV1",
            StartDate = new DateOnly(2023, 1, 1),
            EndDate = new DateOnly(2023, 12, 31),
            Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true
        };
        context.GtfsCalendars.Add(calendar);

        var route = new GtfsRoute { GtfsImportRunId = runId, RouteId = "R1", RouteShortName = "100", RouteType = 3 };
        context.GtfsRoutes.Add(route);

        var trip = new GtfsTrip { GtfsImportRunId = runId, Route = route, TripId = "T1", RouteId = "R1", ServiceId = "SRV1", DirectionId = 0 };
        context.GtfsTrips.Add(trip);

        var stopA = new GtfsStop { GtfsImportRunId = runId, StopId = $"A_{runId}", StopName = "Origin", StopLat = 41.01, StopLon = 29.01 };
        var stopB = new GtfsStop { GtfsImportRunId = runId, StopId = $"B_{runId}", StopName = "Intermediate 1", StopLat = 41.02, StopLon = 29.02 };
        var stopC = new GtfsStop { GtfsImportRunId = runId, StopId = $"C_{runId}", StopName = "Intermediate 2", StopLat = 41.03, StopLon = 29.03 };
        var stopD = new GtfsStop { GtfsImportRunId = runId, StopId = $"D_{runId}", StopName = "Intermediate 3", StopLat = 41.04, StopLon = 29.04 };
        var stopE = new GtfsStop { GtfsImportRunId = runId, StopId = $"E_{runId}", StopName = "Dest", StopLat = 41.05, StopLon = 29.05 };
        context.GtfsStops.AddRange(stopA, stopB, stopC, stopD, stopE);

        // Map them to Domain.Stop as well for walking calculations
        context.Stops.AddRange(
            new Stop { ExternalStopId = $"A_{runId}", Name = "Origin", Latitude = 41.01, Longitude = 29.01 },
            new Stop { ExternalStopId = $"B_{runId}", Name = "Intermediate 1", Latitude = 41.02, Longitude = 29.02 },
            new Stop { ExternalStopId = $"C_{runId}", Name = "Intermediate 2", Latitude = 41.03, Longitude = 29.03 },
            new Stop { ExternalStopId = $"D_{runId}", Name = "Intermediate 3", Latitude = 41.04, Longitude = 29.04 },
            new Stop { ExternalStopId = $"E_{runId}", Name = "Dest", Latitude = 41.05, Longitude = 29.05 }
        );

        context.GtfsStopTimes.AddRange(
            new GtfsStopTime { GtfsImportRunId = runId, Trip = trip, TripId = "T1", Stop = stopA, StopId = $"A_{runId}", StopSequence = 1, ArrivalSeconds = 36000, DepartureSeconds = 36000, ArrivalTimeRaw = "10:00:00", DepartureTimeRaw = "10:00:00" },
            new GtfsStopTime { GtfsImportRunId = runId, Trip = trip, TripId = "T1", Stop = stopB, StopId = $"B_{runId}", StopSequence = 2, ArrivalSeconds = 36600, DepartureSeconds = 36600, ArrivalTimeRaw = "10:10:00", DepartureTimeRaw = "10:10:00" },
            new GtfsStopTime { GtfsImportRunId = runId, Trip = trip, TripId = "T1", Stop = stopC, StopId = $"C_{runId}", StopSequence = 3, ArrivalSeconds = 37200, DepartureSeconds = 37200, ArrivalTimeRaw = "10:20:00", DepartureTimeRaw = "10:20:00" },
            new GtfsStopTime { GtfsImportRunId = runId, Trip = trip, TripId = "T1", Stop = stopD, StopId = $"D_{runId}", StopSequence = 4, ArrivalSeconds = 37800, DepartureSeconds = 37800, ArrivalTimeRaw = "10:30:00", DepartureTimeRaw = "10:30:00" },
            new GtfsStopTime { GtfsImportRunId = runId, Trip = trip, TripId = "T1", Stop = stopE, StopId = $"E_{runId}", StopSequence = 5, ArrivalSeconds = 38400, DepartureSeconds = 38400, ArrivalTimeRaw = "10:40:00", DepartureTimeRaw = "10:40:00" }
        );

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_WithoutIntermediateStops_ShouldReturnNullAndSaveBandwidth()
    {
        await SeedDatabaseAsync(801);
        var client = _factory.CreateClient();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.01, Lon = 29.01 },
            Destination = new CoordinateDto { Lat = 41.05, Lon = 29.05 },
            DepartureDateTime = new DateTimeOffset(2023, 5, 5, 9, 30, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 0,
            MaxWalkingMeters = 100, // Explicitly 100 to avoid matching D
            IncludeIntermediateStops = false // Explicit false
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonStr = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Payload Size (Include=false): {jsonStr.Length} bytes");

        var result = JsonSerializer.Deserialize<JsonDocument>(jsonStr);
        var itineraries = result!.RootElement.GetProperty("itineraries");
        itineraries.GetArrayLength().Should().BeGreaterThan(0);

        var leg = itineraries[0].GetProperty("legs").EnumerateArray().First(x => x.GetProperty("mode").GetString() == "TRANSIT");
        
        // Assert property exists but is null, or not present
        if (leg.TryGetProperty("intermediateStops", out var intermediateStops))
        {
            intermediateStops.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Search_WithIntermediateStops_ShouldReturnOrderedStopsAndMeasureBandwidth()
    {
        await SeedDatabaseAsync(802);
        var client = _factory.CreateClient();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.01, Lon = 29.01 },
            Destination = new CoordinateDto { Lat = 41.05, Lon = 29.05 },
            DepartureDateTime = new DateTimeOffset(2023, 5, 5, 9, 30, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 0,
            MaxWalkingMeters = 100, // Explicitly 100 to avoid matching D
            IncludeIntermediateStops = true // Explicit true
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var jsonStr = await response.Content.ReadAsStringAsync();
        
        if (response.StatusCode != HttpStatusCode.OK)
        {
            _output.WriteLine($"ERROR 500 Response: {jsonStr}");
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _output.WriteLine($"Payload Size (Include=true): {jsonStr.Length} bytes");

        var result = JsonSerializer.Deserialize<JsonDocument>(jsonStr);
        var itineraries = result!.RootElement.GetProperty("itineraries");
        itineraries.GetArrayLength().Should().BeGreaterThan(0);

        var leg = itineraries[0].GetProperty("legs").EnumerateArray().First(x => x.GetProperty("mode").GetString() == "TRANSIT");
        
        var intermediateStops = leg.GetProperty("intermediateStops");
        intermediateStops.ValueKind.Should().Be(JsonValueKind.Array);
        intermediateStops.GetArrayLength().Should().Be(3); // B, C, D (A is origin, E is dest)

        var arr = intermediateStops.EnumerateArray().ToArray();
        arr[0].GetProperty("stopId").GetString().Should().Be("B_802");
        arr[0].GetProperty("stopSequence").GetInt32().Should().Be(2);
        
        arr[1].GetProperty("stopId").GetString().Should().Be("C_802");
        arr[1].GetProperty("stopSequence").GetInt32().Should().Be(3);
        
        arr[2].GetProperty("stopId").GetString().Should().Be("D_802");
        arr[2].GetProperty("stopSequence").GetInt32().Should().Be(4);

        // Validate time fields
        arr[0].GetProperty("rawGtfsArrivalTime").GetString().Should().Be("10:10:00");
        arr[0].GetProperty("arrivalSeconds").GetInt32().Should().Be(36600);
        arr[0].GetProperty("arrivalTime").GetString().Should().NotBeNullOrEmpty();
    }
}
