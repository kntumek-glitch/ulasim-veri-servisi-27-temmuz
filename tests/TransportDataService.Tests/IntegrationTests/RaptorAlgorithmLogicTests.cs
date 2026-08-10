using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
public class RaptorAlgorithmLogicTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private int _runId;

    private static int _seedRunId = 0;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public RaptorAlgorithmLogicTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seedRunId != 0)
            {
                _runId = _seedRunId;
                return;
            }
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var run = new GtfsImportRun
            {
                FileHash = "RAPTOR_ALGO_LOGIC_HASH",
                IsActive = true,
                Status = "Completed",
                StartedAt = DateTime.UtcNow
            };
            db.GtfsImportRuns.Add(run);
            await db.SaveChangesAsync();
            _seedRunId = run.Id;

            await SeedAlgorithmEdgeCaseDataAsync(db, _seedRunId);
            await db.SaveChangesAsync();

            var transferService = scope.ServiceProvider.GetRequiredService<IGtfsTransferCalculationService>();
            await transferService.CalculateTransfersAsync(_seedRunId, CancellationToken.None);
            
            _runId = _seedRunId;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAlgorithmEdgeCaseDataAsync(AppDbContext db, int runId)
    {
        db.GtfsAgencies.Add(new GtfsAgency { AgencyId = "AG_ALGO", AgencyName = "Algo Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRunId = runId });
        db.GtfsCalendars.Add(new GtfsCalendar { ServiceId = "SRV_ALGO", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31), GtfsImportRunId = runId });

        // Stops for Spatial vs Temporal Paradox
        var sO1 = new GtfsStop { StopId = "S_O1", StopName = "Origin Far", StopLat = 41.005, StopLon = 29.005, GtfsImportRunId = runId }; // ~780m
        var sO2 = new GtfsStop { StopId = "S_O2", StopName = "Origin Close", StopLat = 41.001, StopLon = 29.001, GtfsImportRunId = runId }; // ~150m
        var sD = new GtfsStop { StopId = "S_D", StopName = "Dest", StopLat = 41.020, StopLon = 29.020, GtfsImportRunId = runId };
        
        // Stops for Transfer Domination
        var sTO = new GtfsStop { StopId = "T_O", StopName = "Trans Origin", StopLat = 41.030, StopLon = 29.030, GtfsImportRunId = runId };
        var sTT = new GtfsStop { StopId = "T_T", StopName = "Trans Hub", StopLat = 41.040, StopLon = 29.040, GtfsImportRunId = runId };
        var sTD = new GtfsStop { StopId = "T_D", StopName = "Trans Dest", StopLat = 41.050, StopLon = 29.050, GtfsImportRunId = runId };

        db.GtfsStops.AddRange(sO1, sO2, sD, sTO, sTT, sTD);

        var rFast = new GtfsRoute { RouteId = "R_FAST", RouteShortName = "FAST", RouteType = 3, GtfsImportRunId = runId };
        var rSlow = new GtfsRoute { RouteId = "R_SLOW", RouteShortName = "SLOW", RouteType = 3, GtfsImportRunId = runId };
        db.GtfsRoutes.AddRange(rFast, rSlow);

        var t1 = new GtfsTrip { Route = rFast, TripId = "TRIP_O1_D", RouteId = "R_FAST", ServiceId = "SRV_ALGO", DirectionId = 0, GtfsImportRunId = runId };
        var t2 = new GtfsTrip { Route = rSlow, TripId = "TRIP_O2_D", RouteId = "R_SLOW", ServiceId = "SRV_ALGO", DirectionId = 0, GtfsImportRunId = runId };
        
        db.GtfsTrips.AddRange(t1, t2);

        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t1, Stop = sO1, TripId = "TRIP_O1_D", StopId = "S_O1", StopSequence = 1, ArrivalSeconds = 8 * 3600, DepartureSeconds = 8 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1, Stop = sD, TripId = "TRIP_O1_D", StopId = "S_D", StopSequence = 2, ArrivalSeconds = 8 * 3600 + 900, DepartureSeconds = 8 * 3600 + 900, GtfsImportRunId = runId },
            
            new GtfsStopTime { Trip = t2, Stop = sO2, TripId = "TRIP_O2_D", StopId = "S_O2", StopSequence = 1, ArrivalSeconds = 8 * 3600 + 600, DepartureSeconds = 8 * 3600 + 600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t2, Stop = sD, TripId = "TRIP_O2_D", StopId = "S_D", StopSequence = 2, ArrivalSeconds = 8 * 3600 + 1800, DepartureSeconds = 8 * 3600 + 1800, GtfsImportRunId = runId }
        );

        var tDir = new GtfsTrip { Route = rSlow, TripId = "TRIP_DIR", RouteId = "R_SLOW", ServiceId = "SRV_ALGO", DirectionId = 0, GtfsImportRunId = runId };
        var tTrans1 = new GtfsTrip { Route = rFast, TripId = "TRIP_TR1", RouteId = "R_FAST", ServiceId = "SRV_ALGO", DirectionId = 0, GtfsImportRunId = runId };
        var tTrans2 = new GtfsTrip { Route = rFast, TripId = "TRIP_TR2", RouteId = "R_FAST", ServiceId = "SRV_ALGO", DirectionId = 0, GtfsImportRunId = runId };
        
        db.GtfsTrips.AddRange(tDir, tTrans1, tTrans2);

        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = tDir, Stop = sTO, TripId = "TRIP_DIR", StopId = "T_O", StopSequence = 1, ArrivalSeconds = 9 * 3600, DepartureSeconds = 9 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = tDir, Stop = sTD, TripId = "TRIP_DIR", StopId = "T_D", StopSequence = 2, ArrivalSeconds = 10 * 3600, DepartureSeconds = 10 * 3600, GtfsImportRunId = runId },
            
            new GtfsStopTime { Trip = tTrans1, Stop = sTO, TripId = "TRIP_TR1", StopId = "T_O", StopSequence = 1, ArrivalSeconds = 9 * 3600 + 600, DepartureSeconds = 9 * 3600 + 600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = tTrans1, Stop = sTT, TripId = "TRIP_TR1", StopId = "T_T", StopSequence = 2, ArrivalSeconds = 9 * 3600 + 1200, DepartureSeconds = 9 * 3600 + 1200, GtfsImportRunId = runId },
            
            new GtfsStopTime { Trip = tTrans2, Stop = sTT, TripId = "TRIP_TR2", StopId = "T_T", StopSequence = 1, ArrivalSeconds = 9 * 3600 + 1500, DepartureSeconds = 9 * 3600 + 1500, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = tTrans2, Stop = sTD, TripId = "TRIP_TR2", StopId = "T_D", StopSequence = 2, ArrivalSeconds = 9 * 3600 + 2100, DepartureSeconds = 9 * 3600 + 2100, GtfsImportRunId = runId }
        );

        var sN1 = new GtfsStop { StopId = "N_1", StopName = "N1", StopLat = 41.060, StopLon = 29.060, GtfsImportRunId = runId };
        var sN2 = new GtfsStop { StopId = "N_2", StopName = "N2", StopLat = 41.070, StopLon = 29.070, GtfsImportRunId = runId };
        db.GtfsStops.AddRange(sN1, sN2);
        
        var tSlowN = new GtfsTrip { Route = rSlow, TripId = "TRIP_SLOW_N", RouteId = "R_SLOW", ServiceId = "SRV_ALGO", DirectionId = 0, GtfsImportRunId = runId };
        var tExpN = new GtfsTrip { Route = rFast, TripId = "TRIP_EXP_N", RouteId = "R_FAST", ServiceId = "SRV_ALGO", DirectionId = 0, GtfsImportRunId = runId };
        
        db.GtfsTrips.AddRange(tSlowN, tExpN);
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = tSlowN, Stop = sN1, TripId = "TRIP_SLOW_N", StopId = "N_1", StopSequence = 1, ArrivalSeconds = 11 * 3600, DepartureSeconds = 11 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = tSlowN, Stop = sN2, TripId = "TRIP_SLOW_N", StopId = "N_2", StopSequence = 2, ArrivalSeconds = 12 * 3600, DepartureSeconds = 12 * 3600, GtfsImportRunId = runId },
            
            new GtfsStopTime { Trip = tExpN, Stop = sN1, TripId = "TRIP_EXP_N", StopId = "N_1", StopSequence = 1, ArrivalSeconds = 11 * 3600 + 900, DepartureSeconds = 11 * 3600 + 900, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = tExpN, Stop = sN2, TripId = "TRIP_EXP_N", StopId = "N_2", StopSequence = 2, ArrivalSeconds = 11 * 3600 + 2700, DepartureSeconds = 11 * 3600 + 2700, GtfsImportRunId = runId }
        );
    }

    [Fact]
    public async Task Search_SpatialVsTemporalParadox_PrioritizesFasterGlobalArrival()
    {
        var client = _factory.CreateClient();
        var req = new JourneyPlanV2SearchRequest
        {
            Origin = new GeoCoordinate { Lat = 41.000, Lon = 29.000 },
            Destination = new GeoCoordinate { Lat = 41.020, Lon = 29.020 },
            DateTime = new DateTime(2024, 5, 5, 7, 50, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 2000,
            IncludeWalkingGeometry = false
        };

        var response = await client.PostAsJsonAsync("/api/v2/journey-plans/search", req);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        res.Should().NotBeNull();
        res!.Itineraries.Should().NotBeEmpty();
        
        var bestItin = res.Itineraries.First();
        bestItin.Legs.Should().Contain(l => l.Mode == "TRANSIT");
        var transitLeg = bestItin.Legs.First(l => l.Mode == "TRANSIT");

        transitLeg.TripId.Should().Be("TRIP_O1_D");
    }

    [Fact]
    public async Task Search_TransferDomination_OneTransferBeatsZeroTransfer()
    {
        var client = _factory.CreateClient();
        var req = new JourneyPlanV2SearchRequest
        {
            Origin = new GeoCoordinate { Lat = 41.030, Lon = 29.030 },
            Destination = new GeoCoordinate { Lat = 41.050, Lon = 29.050 },
            DateTime = new DateTime(2024, 5, 5, 8, 55, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 2000
        };

        var response = await client.PostAsJsonAsync("/api/v2/journey-plans/search", req);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        var bestItin = res!.Itineraries.First();
        var transitLegs = bestItin.Legs.Where(l => l.Mode == "TRANSIT").ToList();
        transitLegs.Should().HaveCount(2);
        transitLegs[0].TripId.Should().Be("TRIP_TR1");
        transitLegs[1].TripId.Should().Be("TRIP_TR2");
    }

    [Fact]
    public async Task Search_OvertakingTrips_ExpressTripBeatsEarlierSlowTrip()
    {
        var client = _factory.CreateClient();
        var req = new JourneyPlanV2SearchRequest
        {
            Origin = new GeoCoordinate { Lat = 41.060, Lon = 29.060 },
            Destination = new GeoCoordinate { Lat = 41.070, Lon = 29.070 },
            DateTime = new DateTime(2024, 5, 5, 10, 50, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 1000
        };

        var response = await client.PostAsJsonAsync("/api/v2/journey-plans/search", req);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        var bestItin = res!.Itineraries.First();
        var transitLeg = bestItin.Legs.First(l => l.Mode == "TRANSIT");
        transitLeg.TripId.Should().Be("TRIP_EXP_N");
    }
}
