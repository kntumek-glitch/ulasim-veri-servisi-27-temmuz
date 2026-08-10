using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class RaptorModeAndLimitTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private int _runId;
    private static int _seedRunId = 0;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public RaptorModeAndLimitTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seedRunId != 0) { _runId = _seedRunId; return; }
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var run = new GtfsImportRun { FileHash = "RAPTOR_MODE_HASH", IsActive = true, Status = "Completed", StartedAt = DateTime.UtcNow };
            db.GtfsImportRuns.Add(run);
            await db.SaveChangesAsync();
            _seedRunId = run.Id;

            await SeedModeDataAsync(db, _seedRunId);
            await db.SaveChangesAsync();

            var transferService = scope.ServiceProvider.GetRequiredService<IGtfsTransferCalculationService>();
            await transferService.CalculateTransfersAsync(_seedRunId, CancellationToken.None);
            
            _runId = _seedRunId;
        }
        finally { _seedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedModeDataAsync(AppDbContext db, int runId)
    {
        db.GtfsAgencies.Add(new GtfsAgency { AgencyId = "AG_MODE", AgencyName = "Mode Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRunId = runId });
        db.GtfsCalendars.Add(new GtfsCalendar { ServiceId = "SRV_MODE", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31), GtfsImportRunId = runId });
        
        var sO = new GtfsStop { StopId = "S_O", StopName = "Origin", StopLat = 41.010, StopLon = 29.010, GtfsImportRunId = runId };
        var sD = new GtfsStop { StopId = "S_D", StopName = "Dest", StopLat = 41.030, StopLon = 29.030, GtfsImportRunId = runId };
        
        // Very far stop for testing MaxWalk
        var sFar = new GtfsStop { StopId = "S_FAR", StopName = "Far Dest", StopLat = 41.150, StopLon = 29.150, GtfsImportRunId = runId }; // ~15+ km away
        db.GtfsStops.AddRange(sO, sD, sFar);

        var route = new GtfsRoute { RouteId = "R_MODE", RouteShortName = "MODE", RouteType = 3, GtfsImportRunId = runId };
        db.GtfsRoutes.Add(route);

        // Trip 1: Early departure, Early Arrival
        var t1 = new GtfsTrip { Route = route, TripId = "TRIP_EARLY", RouteId = "R_MODE", ServiceId = "SRV_MODE", DirectionId = 0, GtfsImportRunId = runId };
        // Trip 2: Late departure, Late Arrival (but before the ARRIVE_BY constraint)
        var t2 = new GtfsTrip { Route = route, TripId = "TRIP_LATE", RouteId = "R_MODE", ServiceId = "SRV_MODE", DirectionId = 0, GtfsImportRunId = runId };
        
        // Trip for max walking distance test
        var tFar = new GtfsTrip { Route = route, TripId = "TRIP_FAR", RouteId = "R_MODE", ServiceId = "SRV_MODE", DirectionId = 1, GtfsImportRunId = runId };
        db.GtfsTrips.AddRange(t1, t2, tFar);

        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t1, Stop = sO, TripId = "TRIP_EARLY", StopId = "S_O", StopSequence = 1, ArrivalSeconds = 8 * 3600, DepartureSeconds = 8 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1, Stop = sD, TripId = "TRIP_EARLY", StopId = "S_D", StopSequence = 2, ArrivalSeconds = 9 * 3600, DepartureSeconds = 9 * 3600, GtfsImportRunId = runId },
            
            new GtfsStopTime { Trip = t2, Stop = sO, TripId = "TRIP_LATE", StopId = "S_O", StopSequence = 1, ArrivalSeconds = 12 * 3600, DepartureSeconds = 12 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t2, Stop = sD, TripId = "TRIP_LATE", StopId = "S_D", StopSequence = 2, ArrivalSeconds = 13 * 3600, DepartureSeconds = 13 * 3600, GtfsImportRunId = runId },
            
            new GtfsStopTime { Trip = tFar, Stop = sO, TripId = "TRIP_FAR", StopId = "S_O", StopSequence = 1, ArrivalSeconds = 10 * 3600, DepartureSeconds = 10 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = tFar, Stop = sFar, TripId = "TRIP_FAR", StopId = "S_FAR", StopSequence = 2, ArrivalSeconds = 11 * 3600, DepartureSeconds = 11 * 3600, GtfsImportRunId = runId }
        );
    }

    [Fact]
    public async Task Search_ArriveBy_MaximizesDepartureTime()
    {
        var client = _factory.CreateClient();
        
        // Target arrival: 14:00. Both TRIP_EARLY (arr 09:00) and TRIP_LATE (arr 13:00) can make it.
        // In ARRIVE_BY, the engine MUST select TRIP_LATE because it allows the user to depart much later (12:00 vs 08:00).
        var req = new JourneyPlanV2SearchRequest
        {
            Origin = new GeoCoordinate { Lat = 41.010, Lon = 29.010 },
            Destination = new GeoCoordinate { Lat = 41.030, Lon = 29.030 },
            DateTime = new DateTime(2024, 6, 6, 14, 0, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.ARRIVE_BY,
            MaxWalkingMeters = 2000
        };

        var response = await client.PostAsJsonAsync("/api/v2/journey-plans/search", req);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        res.Should().NotBeNull();
        res!.Itineraries.Should().NotBeEmpty();
        
        var bestItin = res.Itineraries.First();
        var transitLeg = bestItin.Legs.First(l => l.Mode == "TRANSIT");
        
        // TRIP_LATE should be selected over TRIP_EARLY
        transitLeg.TripId.Should().Be("TRIP_LATE");
        bestItin.ArrivalTime.Should().BeOnOrBefore(new DateTime(2024, 6, 6, 14, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Search_MaxWalkingLimit_PrunesWhenDistanceExceeded()
    {
        var client = _factory.CreateClient();
        
        // Origin is fine. Destination is near S_FAR (41.150, 29.150).
        // Let's set MaxWalkingMeters very low.
        var reqFail = new JourneyPlanV2SearchRequest
        {
            Origin = new GeoCoordinate { Lat = 41.010, Lon = 29.010 },
            Destination = new GeoCoordinate { Lat = 41.160, Lon = 29.160 }, // Even further!
            DateTime = new DateTime(2024, 6, 6, 8, 0, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 500 // Too small to reach S_FAR
        };

        var resFail = await client.PostAsJsonAsync("/api/v2/journey-plans/search", reqFail);
        var bodyFail = await resFail.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        // Should yield NO_NEARBY_DESTINATION_STOP or NO_ROUTE_FOUND based on how early it prunes
        bodyFail!.ReasonCode.Should().BeOneOf("NO_NEARBY_DESTINATION_STOP", "NO_ROUTE_FOUND");

        // Set high max walking meters -> should find the route
        var reqPass = new JourneyPlanV2SearchRequest
        {
            Origin = new GeoCoordinate { Lat = 41.010, Lon = 29.010 },
            Destination = new GeoCoordinate { Lat = 41.160, Lon = 29.160 },
            DateTime = new DateTime(2024, 6, 6, 8, 0, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 15000 // Very large
        };

        var resPass = await client.PostAsJsonAsync("/api/v2/journey-plans/search", reqPass);
        resPass.EnsureSuccessStatusCode();
        var bodyPass = await resPass.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        bodyPass!.Itineraries.Should().NotBeEmpty();
    }
}
