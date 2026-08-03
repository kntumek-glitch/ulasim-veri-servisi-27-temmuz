using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

public class ItinerarySummaryIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ItinerarySummaryIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnCorrectSummaryFields_ForDirectTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var journeyService = scope.ServiceProvider.GetRequiredService<IJourneyPlanningService>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var runId = new Random().Next(10000, 99999);
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

        var orgStopId = $"ORG_{runId}";
        var dstStopId = $"DST_{runId}";
        
        var stopOrg = new GtfsStop { GtfsImportRunId = runId, StopId = orgStopId, StopName = "Origin", StopLat = 41.0, StopLon = 29.0 };
        var stopDst = new GtfsStop { GtfsImportRunId = runId, StopId = dstStopId, StopName = "Dest", StopLat = 41.01, StopLon = 29.01 };
        context.GtfsStops.AddRange(stopOrg, stopDst);

        context.Stops.AddRange(
            new Stop { ExternalStopId = orgStopId, Name = "Origin", Latitude = 41.0, Longitude = 29.0 },
            new Stop { ExternalStopId = dstStopId, Name = "Dest", Latitude = 41.01, Longitude = 29.01 }
        );

        var route = new GtfsRoute { GtfsImportRunId = runId, RouteId = $"R1_{runId}", RouteShortName = "Bus R1", RouteType = 3 }; // Bus
        context.GtfsRoutes.Add(route);

        var trip = new GtfsTrip { GtfsImportRunId = runId, TripId = $"T1_{runId}", Route = route, RouteId = route.RouteId, ServiceId = $"S1_{runId}", DirectionId = 0 };
        context.GtfsTrips.Add(trip);
        
        context.GtfsStopTimes.Add(new GtfsStopTime { GtfsImportRunId = runId, Trip = trip, TripId = trip.TripId, Stop = stopOrg, StopId = orgStopId, StopSequence = 1, DepartureSeconds = 36000, DepartureTimeRaw = "10:00:00", ArrivalSeconds = 36000, ArrivalTimeRaw = "10:00:00" }); 
        context.GtfsStopTimes.Add(new GtfsStopTime { GtfsImportRunId = runId, Trip = trip, TripId = trip.TripId, Stop = stopDst, StopId = dstStopId, StopSequence = 2, DepartureSeconds = 37800, DepartureTimeRaw = "10:30:00", ArrivalSeconds = 37800, ArrivalTimeRaw = "10:30:00" });   

        context.GtfsCalendars.Add(new GtfsCalendar { GtfsImportRunId = runId, ServiceId = $"S1_{runId}", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2024,1,1), EndDate = new DateOnly(2025,12,31) });

        await context.SaveChangesAsync();
        
        var req = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.0, Lon = 29.0 },
            Destination = new CoordinateDto { Lat = 41.01, Lon = 29.01 },
            DepartureDateTime = new DateTimeOffset(2024, 5, 20, 9, 50, 0, TimeSpan.FromHours(3)), 
            MaxTransfers = 0
        };

        var res = await journeyService.SearchJourneyAsync(req);
        
        Assert.NotNull(res);
        Assert.NotEmpty(res.Itineraries);
        var itinerary = res.Itineraries.First();
        
        Assert.Equal("STATIC_GTFS", itinerary.DataSource);
        Assert.Equal("Bus", itinerary.RouteTypeSummary);
        Assert.Equal(0, itinerary.TransferCount);
        Assert.Equal(1, itinerary.TotalTransitStopCount); 
        
        // InitialWaitTime: 10 mins (09:50 -> 10:00)
        Assert.Equal(600, itinerary.InitialWaitTimeSeconds);
        Assert.Empty(itinerary.TransferWaitTimes);
        Assert.Equal(600, itinerary.TotalWaitingTimeSeconds);
        Assert.Equal(1800, itinerary.TotalInVehicleTimeSeconds); // 10:00 to 10:30
    }
}
