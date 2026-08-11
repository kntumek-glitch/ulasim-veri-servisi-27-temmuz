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
public class RaptorTemporalConstraintTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private int _runId;
    private static int _seedRunId = 0;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public RaptorTemporalConstraintTests(CustomWebApplicationFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seedRunId != 0)
            {
                _runId = _seedRunId;
                using var innerScope = _factory.Services.CreateScope();
                var sm = innerScope.ServiceProvider.GetRequiredService<ulasim_veri_servisi.Services.Interfaces.IRoutingSnapshotManager>();
                var innerCandidate = await sm.BuildCandidateSnapshotAsync(_seedRunId, "RAPTOR_TEMP_HASH", System.Threading.CancellationToken.None);
                sm.PromoteSnapshot(innerCandidate);
                return;
            }
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await db.GtfsImportRuns.ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));
            var run = new GtfsImportRun { FileHash = "RAPTOR_TEMP_HASH", IsActive = true, Status = "Completed", StartedAt = DateTime.UtcNow };
            db.GtfsImportRuns.Add(run);
            await db.SaveChangesAsync();
            _seedRunId = run.Id;

            await SeedTemporalDataAsync(db, _seedRunId);
            await db.SaveChangesAsync();

            var transferService = scope.ServiceProvider.GetRequiredService<IGtfsTransferCalculationService>();
            await transferService.CalculateTransfersAsync(_seedRunId, CancellationToken.None);
            
            var snapshotManager = scope.ServiceProvider.GetRequiredService<ulasim_veri_servisi.Services.Interfaces.IRoutingSnapshotManager>();
            var candidate = await snapshotManager.BuildCandidateSnapshotAsync(_seedRunId, "RAPTOR_TEMP_HASH", CancellationToken.None);
            snapshotManager.PromoteSnapshot(candidate);
            
            _runId = _seedRunId;
        }
        finally { _seedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedTemporalDataAsync(AppDbContext db, int runId)
    {
        db.GtfsAgencies.Add(new GtfsAgency { AgencyId = "AG_TEMP", AgencyName = "Temp Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRunId = runId });
        
        // Baseline Calendar (Valid only on Mondays)
        db.GtfsCalendars.Add(new GtfsCalendar { ServiceId = "SRV_TEMP_MON", Monday = true, Tuesday = false, Wednesday = false, Thursday = false, Friday = false, Saturday = false, Sunday = false, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31), GtfsImportRunId = runId });
        
        // Exception: 2024-05-07 (Tuesday) is ADDED
        // Exception: 2024-05-06 (Monday) is REMOVED
        db.GtfsCalendarDates.AddRange(
            new GtfsCalendarDate { ServiceId = "SRV_TEMP_MON", Date = new DateOnly(2024, 5, 7), ExceptionType = 1, GtfsImportRunId = runId }, // 1 = Added
            new GtfsCalendarDate { ServiceId = "SRV_TEMP_MON", Date = new DateOnly(2024, 5, 6), ExceptionType = 2, GtfsImportRunId = runId }  // 2 = Removed
        );

        var sO = new GtfsStop { StopId = "S_O", StopName = "Origin", StopLat = 41.010, StopLon = 29.010, GtfsImportRunId = runId };
        var sT = new GtfsStop { StopId = "S_T", StopName = "Transfer", StopLat = 41.020, StopLon = 29.020, GtfsImportRunId = runId };
        var sD = new GtfsStop { StopId = "S_D", StopName = "Dest", StopLat = 41.030, StopLon = 29.030, GtfsImportRunId = runId };
        db.GtfsStops.AddRange(sO, sT, sD);

        var rNight = new GtfsRoute { RouteId = "R_NIGHT", RouteShortName = "NIGHT", RouteType = 3, GtfsImportRunId = runId };
        db.GtfsRoutes.Add(rNight);

        // Midnight Overflow Trip: Departs at 24:30:00, Arrives at 25:30:00
        var tNight = new GtfsTrip { Route = rNight, TripId = "TRIP_NIGHT", RouteId = "R_NIGHT", ServiceId = "SRV_TEMP_MON", DirectionId = 0, GtfsImportRunId = runId };
        db.GtfsTrips.Add(tNight);

        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = tNight, Stop = sO, TripId = "TRIP_NIGHT", StopId = "S_O", StopSequence = 1, ArrivalSeconds = 88200, DepartureSeconds = 88200, GtfsImportRunId = runId }, // 24:30:00 (88200 = 24.5h)
            new GtfsStopTime { Trip = tNight, Stop = sD, TripId = "TRIP_NIGHT", StopId = "S_D", StopSequence = 2, ArrivalSeconds = 91800, DepartureSeconds = 91800, GtfsImportRunId = runId }  // 25:30:00 (91800 = 25.5h)
        );

        // Buffer Fallback Trips
        var tBuf1 = new GtfsTrip { Route = rNight, TripId = "TRIP_BUF1", RouteId = "R_NIGHT", ServiceId = "SRV_TEMP_MON", DirectionId = 0, GtfsImportRunId = runId };
        // Transfer is at S_T. Walk takes ~15 mins. Buffer is 2 mins (120s). Total needed = 17 mins.
        db.GtfsTrips.Add(tBuf1);
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = tBuf1, Stop = sO, TripId = "TRIP_BUF1", StopId = "S_O", StopSequence = 1, ArrivalSeconds = 12 * 3600, DepartureSeconds = 12 * 3600, GtfsImportRunId = runId }, // 12:00
            new GtfsStopTime { Trip = tBuf1, Stop = sT, TripId = "TRIP_BUF1", StopId = "S_T", StopSequence = 2, ArrivalSeconds = 13 * 3600, DepartureSeconds = 13 * 3600, GtfsImportRunId = runId }  // 13:00
        );
        
        // Connects to S_T. 
        // Invalid Trip: Leaves at 13:16 (Misses 17m constraint by 1 min)
        var tBuf2_Invalid = new GtfsTrip { Route = rNight, TripId = "TRIP_BUF2_INV", RouteId = "R_NIGHT", ServiceId = "SRV_TEMP_MON", DirectionId = 0, GtfsImportRunId = runId };
        // Valid Trip: Leaves at 13:20 (Satisfies constraint)
        var tBuf2_Valid = new GtfsTrip { Route = rNight, TripId = "TRIP_BUF2_VAL", RouteId = "R_NIGHT", ServiceId = "SRV_TEMP_MON", DirectionId = 0, GtfsImportRunId = runId };
        db.GtfsTrips.AddRange(tBuf2_Invalid, tBuf2_Valid);
        
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = tBuf2_Invalid, Stop = sT, TripId = "TRIP_BUF2_INV", StopId = "S_T", StopSequence = 1, ArrivalSeconds = 13 * 3600 + 960, DepartureSeconds = 13 * 3600 + 960, GtfsImportRunId = runId }, // 13:16
            new GtfsStopTime { Trip = tBuf2_Invalid, Stop = sD, TripId = "TRIP_BUF2_INV", StopId = "S_D", StopSequence = 2, ArrivalSeconds = 14 * 3600, DepartureSeconds = 14 * 3600, GtfsImportRunId = runId },
            
            new GtfsStopTime { Trip = tBuf2_Valid, Stop = sT, TripId = "TRIP_BUF2_VAL", StopId = "S_T", StopSequence = 1, ArrivalSeconds = 13 * 3600 + 1200, DepartureSeconds = 13 * 3600 + 1200, GtfsImportRunId = runId }, // 13:20
            new GtfsStopTime { Trip = tBuf2_Valid, Stop = sD, TripId = "TRIP_BUF2_VAL", StopId = "S_D", StopSequence = 2, ArrivalSeconds = 14 * 3600 + 600, DepartureSeconds = 14 * 3600 + 600, GtfsImportRunId = runId }
        );
    }

    [Fact]
    public async Task Search_MidnightOverflows_CorrectlyResolvesToNextDay()
    {
        var client = _factory.CreateClient();
        // 2024-05-07 is Tuesday (ADDED exception). Trip runs on 2024-05-07, meaning it departs at 24:30.
        // If user searches on 2024-05-08 (Wednesday) at 00:15, they should catch the rollover trip from Tuesday!
        var req = new JourneyPlanV2SearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 }, // S_O
            Destination = new CoordinateDto { Lat = 41.030, Lon = 29.030 }, // S_D
            DateTime = new DateTime(2024, 5, 8, 0, 15, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 1000
        };

        var response = await client.PostAsJsonAsync("/api/v2/journey-plans/search", req);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        var bestItin = res!.Itineraries.First();
        var transitLeg = bestItin.Legs.First(l => l.Mode == "TRANSIT");
        transitLeg.TripId.Should().Be("TRIP_NIGHT");
        
        // Verify time mapping (Departs May 8 at 00:30 Local, which is May 7 21:30 UTC)
        transitLeg.DepartureTime.Value.ToUniversalTime().Should().BeOnOrAfter(new DateTimeOffset(2024, 5, 7, 21, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Search_ServiceExceptions_AdheresToCalendarDates()
    {
        var client = _factory.CreateClient();
        
        // 2024-05-06 is Monday (REMOVED exception). Even though base calendar says Monday=true, it should fail.
        var reqFail = new JourneyPlanV2SearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 },
            Destination = new CoordinateDto { Lat = 41.030, Lon = 29.030 },
            DateTime = new DateTime(2024, 5, 6, 10, 0, 0, DateTimeKind.Utc), // Should find NO active service
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 1000
        };

        var resFail = await client.PostAsJsonAsync("/api/v2/journey-plans/search", reqFail);
        var bodyFail = await resFail.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        bodyFail!.ReasonCode.Should().BeOneOf("NO_ACTIVE_SERVICE", "NO_ROUTE_FOUND");

        // 2024-05-07 is Tuesday (ADDED exception). It should succeed.
        var reqPass = new JourneyPlanV2SearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 },
            Destination = new CoordinateDto { Lat = 41.030, Lon = 29.030 },
            DateTime = new DateTime(2024, 5, 7, 10, 0, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 1000
        };

        var resPass = await client.PostAsJsonAsync("/api/v2/journey-plans/search", reqPass);
        resPass.EnsureSuccessStatusCode();
        var bodyPass = await resPass.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        bodyPass!.Itineraries.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Search_BufferViolation_FallsBackToNextValidTrip()
    {
        var client = _factory.CreateClient();
        // 2024-05-07 is Tuesday (Active)
        var req = new JourneyPlanV2SearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 }, // S_O
            Destination = new CoordinateDto { Lat = 41.030, Lon = 29.030 }, // S_D
            DateTime = new DateTime(2024, 5, 7, 11, 40, 0, DateTimeKind.Utc),
            SearchMode = RoutingMode.DEPART_AT,
            MaxWalkingMeters = 1000
        };

        var response = await client.PostAsJsonAsync("/api/v2/journey-plans/search", req);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        var bestItin = res!.Itineraries.First();
        var transitLegs = bestItin.Legs.Where(l => l.Mode == "TRANSIT").ToList();
        
        _output.WriteLine("FOUND ITINERARY COUNT: " + res.Itineraries.Count);
        _output.WriteLine("FIRST ITIN LEGS: " + string.Join(", ", res.Itineraries.FirstOrDefault()?.Legs.Select(l => l.Mode) ?? new string[0]));
        transitLegs.Should().HaveCount(2);
        transitLegs[0].TripId.Should().Be("TRIP_BUF1");
        
        // Must be TRIP_BUF2_VAL because TRIP_BUF2_INV violates the transfer buffer.
        transitLegs[1].TripId.Should().Be("TRIP_BUF2_VAL");
    }
}

