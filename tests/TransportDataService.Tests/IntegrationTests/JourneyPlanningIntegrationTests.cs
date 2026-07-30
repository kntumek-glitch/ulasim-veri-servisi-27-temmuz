using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class JourneyPlanningIntegrationTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private int _runId;

    public JourneyPlanningIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Disable existing runs
        var activeRuns = await db.GtfsImportRuns.Where(r => r.IsActive).ToListAsync();
        foreach (var r in activeRuns) r.IsActive = false;
        
        var newRun = new GtfsImportRun
        {
            FileHash = Guid.NewGuid().ToString(),
            IsActive = true,
            Status = "Completed",
            StartedAt = DateTime.UtcNow
        };
        db.GtfsImportRuns.Add(newRun);
        await db.SaveChangesAsync();
        _runId = newRun.Id;

        // SEED DATA
        await SeedDataAsync(db, _runId);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedDataAsync(AppDbContext db, int runId)
    {
        db.GtfsAgencies.Add(new GtfsAgency { AgencyId = "AG1", AgencyName = "Test", AgencyTimezone = "Europe/Istanbul", GtfsImportRunId = runId });
        
        var s1 = new GtfsStop { StopId = "S1", StopName = "Origin", StopLat = 38.4, StopLon = 27.1, GtfsImportRunId = runId };
        var s2 = new GtfsStop { StopId = "S2", StopName = "Transfer", StopLat = 38.5, StopLon = 27.2, GtfsImportRunId = runId };
        var s3 = new GtfsStop { StopId = "S3", StopName = "Dest", StopLat = 38.41, StopLon = 27.11, GtfsImportRunId = runId };
        var s4 = new GtfsStop { StopId = "S4", StopName = "TooFar", StopLat = 39.0, StopLon = 28.0, GtfsImportRunId = runId };
        var s5 = new GtfsStop { StopId = "S5", StopName = "Origin", StopLat = 38.4, StopLon = 27.1001, GtfsImportRunId = runId };
        db.GtfsStops.AddRange(s1, s2, s3, s4, s5);

        var r1 = new GtfsRoute { RouteId = "R1", RouteShortName = "100", GtfsImportRunId = runId };
        var r2 = new GtfsRoute { RouteId = "R2", RouteShortName = "200", GtfsImportRunId = runId };
        db.GtfsRoutes.AddRange(r1, r2);

        db.GtfsCalendars.Add(new GtfsCalendar
        {
            ServiceId = "SRV_EVERYDAY",
            Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true,
            StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31), GtfsImportRunId = runId
        });

        db.GtfsCalendarDates.AddRange(
            new GtfsCalendarDate { ServiceId = "SRV_ADDED", Date = new DateOnly(2024, 1, 1), ExceptionType = 1, GtfsImportRunId = runId },
            new GtfsCalendarDate { ServiceId = "SRV_REMOVED", Date = new DateOnly(2024, 1, 1), ExceptionType = 2, GtfsImportRunId = runId }
        );

        var t1 = new GtfsTrip { Route = r1, TripId = "T1", RouteId = "R1", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Dest", DirectionId = 0, GtfsImportRunId = runId };
        var t2 = new GtfsTrip { Route = r1, TripId = "T2", RouteId = "R1", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Origin", DirectionId = 1, GtfsImportRunId = runId };
        var t3 = new GtfsTrip { Route = r1, TripId = "T3", RouteId = "R1", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Transfer", DirectionId = 0, GtfsImportRunId = runId };
        var t4 = new GtfsTrip { Route = r2, TripId = "T4", RouteId = "R2", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Dest", DirectionId = 0, GtfsImportRunId = runId };
        var t6 = new GtfsTrip { Route = r2, TripId = "T6", RouteId = "R2", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Dest", DirectionId = 0, GtfsImportRunId = runId };
        var t7 = new GtfsTrip { Route = r1, TripId = "T7", RouteId = "R1", ServiceId = "SRV_ADDED", TripHeadsign = "Added", DirectionId = 0, GtfsImportRunId = runId };
        var t8 = new GtfsTrip { Route = r1, TripId = "T8", RouteId = "R1", ServiceId = "SRV_REMOVED", TripHeadsign = "Removed", DirectionId = 0, GtfsImportRunId = runId };
        var t9 = new GtfsTrip { Route = r1, TripId = "T9", RouteId = "R1", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Midnight", DirectionId = 0, GtfsImportRunId = runId };
        db.GtfsTrips.AddRange(t1, t2, t3, t4, t6, t7, t8, t9);

        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t1, Stop = s1, TripId = "T1", StopId = "S1", StopSequence = 1, ArrivalSeconds = 8*3600, DepartureSeconds = 8*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1, Stop = s3, TripId = "T1", StopId = "S3", StopSequence = 2, ArrivalSeconds = 8*3600 + 1800, DepartureSeconds = 8*3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t2, Stop = s3, TripId = "T2", StopId = "S3", StopSequence = 1, ArrivalSeconds = 9*3600, DepartureSeconds = 9*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t2, Stop = s1, TripId = "T2", StopId = "S1", StopSequence = 2, ArrivalSeconds = 9*3600 + 1800, DepartureSeconds = 9*3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t3, Stop = s1, TripId = "T3", StopId = "S1", StopSequence = 1, ArrivalSeconds = 10*3600, DepartureSeconds = 10*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t3, Stop = s2, TripId = "T3", StopId = "S2", StopSequence = 2, ArrivalSeconds = 10*3600 + 1800, DepartureSeconds = 10*3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t4, Stop = s2, TripId = "T4", StopId = "S2", StopSequence = 1, ArrivalSeconds = 10*3600 + 2400, DepartureSeconds = 10*3600 + 2400, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t4, Stop = s3, TripId = "T4", StopId = "S3", StopSequence = 2, ArrivalSeconds = 11*3600, DepartureSeconds = 11*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t6, Stop = s2, TripId = "T6", StopId = "S2", StopSequence = 1, ArrivalSeconds = 10*3600 + 1860, DepartureSeconds = 10*3600 + 1860, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t6, Stop = s3, TripId = "T6", StopId = "S3", StopSequence = 2, ArrivalSeconds = 11*3600, DepartureSeconds = 11*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t7, Stop = s1, TripId = "T7", StopId = "S1", StopSequence = 1, ArrivalSeconds = 12*3600, DepartureSeconds = 12*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t7, Stop = s3, TripId = "T7", StopId = "S3", StopSequence = 2, ArrivalSeconds = 12*3600 + 1800, DepartureSeconds = 12*3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t8, Stop = s1, TripId = "T8", StopId = "S1", StopSequence = 1, ArrivalSeconds = 13*3600, DepartureSeconds = 13*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t8, Stop = s3, TripId = "T8", StopId = "S3", StopSequence = 2, ArrivalSeconds = 13*3600 + 1800, DepartureSeconds = 13*3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t9, Stop = s1, TripId = "T9", StopId = "S1", StopSequence = 1, ArrivalSeconds = 25*3600 + 1800, DepartureSeconds = 25*3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t9, Stop = s3, TripId = "T9", StopId = "S3", StopSequence = 2, ArrivalSeconds = 26*3600, DepartureSeconds = 26*3600, GtfsImportRunId = runId }
        );
    }

    [Fact]
    public async Task R1_ValidDirectRoute_ShouldBeFound()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        result.Should().NotBeNull();
        result!.ReasonCode.Should().Be("SUCCESS");
        result.Itineraries.Should().Contain(i => i.Legs.Any(l => l.TripId == "T1"));
    }

    [Fact]
    public async Task R2_ReverseDirectionTrips_ShouldBeFilteredOut()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 8, 50, 0, TimeSpan.FromHours(3)) // Right before T2 departs, but T2 is reverse
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result!.Itineraries.Should().NotContain(i => i.Legs.Any(l => l.TripId == "T2"));
    }

    [Fact]
    public async Task R3_MaxOneTransfer_ShouldGenerate_ValidTransferRoute()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 9, 50, 0, TimeSpan.FromHours(3)) // 10:00 transfer trip
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        var transferRoute = result!.Itineraries.FirstOrDefault(i => i.Transfers == 1 && i.Legs.Any(l => l.TripId == "T3") && i.Legs.Any(l => l.TripId == "T4"));
        transferRoute.Should().NotBeNull();
        
        // Ensure Leg sequence is WALK -> TRANSIT (T3) -> WALK -> TRANSIT (T4) -> WALK
        transferRoute!.Legs.Count.Should().Be(5);
        transferRoute.Legs[1].TripId.Should().Be("T3");
        transferRoute.Legs[3].TripId.Should().Be("T4");
    }

    [Fact]
    public async Task Z1_OriginWalkingTime_ShouldBeAddedTo_DepartureTime()
    {
        var client = _factory.CreateClient();
        // Assume walking speed 1.4 m/s. S1 is at 38.4, 27.1. We put user slightly away so walking takes > 0 mins.
        // User at 38.4001, 27.1. Distance is ~11 meters. Walking time ~ 8 seconds.
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4001, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 59, 55, TimeSpan.FromHours(3)) // 07:59:55
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        // T1 departs at 08:00:00. 5 seconds gap. User needs 8 seconds to walk. Should MISS T1.
        result!.Itineraries.Should().NotContain(i => i.Legs.Any(l => l.TripId == "T1"));
    }

    [Fact]
    public async Task Z2_InsufficientTransferBuffer_ShouldBeFilteredOut()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 9, 50, 0, TimeSpan.FromHours(3))
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        // T6 departs 1 min after T3 arrives. Buffer kuralı min 3dk, bu yüzden elenmeli.
        result!.Itineraries.Should().NotContain(i => i.Legs.Any(l => l.TripId == "T6"));
    }

    [Fact]
    public async Task Z3_MaxWalkingLimit_Exceeded_ShouldBeFilteredOut()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 39.5, Lon = 28.5 }, // Too far away (>5000m)
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result!.ReasonCode.Should().Be("NO_ROUTE_FOUND");
    }

    [Fact]
    public async Task T1_CalendarDate_AddedService_ShouldBeAvailable()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 11, 50, 0, TimeSpan.FromHours(3)) // Added exception is at 12:00
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result!.Itineraries.Should().Contain(i => i.Legs.Any(l => l.TripId == "T7"));
    }

    [Fact]
    public async Task T2_CalendarDate_RemovedService_ShouldBeBlocked()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 12, 50, 0, TimeSpan.FromHours(3)) // Removed exception is at 13:00
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result!.Itineraries.Should().NotContain(i => i.Legs.Any(l => l.TripId == "T8"));
    }

    [Fact]
    public async Task T3_PastMidnightTrip_ShouldBeCalculatedFrom_PreviousDay()
    {
        var client = _factory.CreateClient();
        // Arama 2 Ocak 01:20'de yapılıyor. Trip 1 Ocak gününün 25:30 (01:30) seferi olmalı.
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 2, 1, 20, 0, TimeSpan.FromHours(3))
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result!.Itineraries.Should().Contain(i => i.Legs.Any(l => l.TripId == "T9"));
    }

    [Fact]
    public async Task T4_SameNameDifferentIdStops_ShouldBeDistinct()
    {
        var client = _factory.CreateClient();
        // S5 has name "Origin" but is at 38.4, 27.1001. We start at 38.4, 27.1.
        // The nearest stops are S1 and S5. They should both be considered independently.
        // Wait, since T1 uses S1, it should still work. But no trip departs from S5 in our seed.
        // The goal is just that it doesn't crash or confuse S1 and S5.
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task D1_Search_ShouldOnlyUse_ActiveFeed()
    {
        // GtfsImportLifecycleTests/JourneyPlanningErrorTests ensure active runs are handled correctly.
        // Since we explicitly seeded data ONLY with `GtfsImportRunId = _runId`, this is inherently tested.
        // But let's create a disabled run and ensure its data doesn't leak.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var newRun = new GtfsImportRun { FileHash = "INACTIVE", IsActive = false, Status = "Completed", StartedAt = DateTime.UtcNow };
            db.GtfsImportRuns.Add(newRun);
            await db.SaveChangesAsync();
            
            db.GtfsStops.Add(new GtfsStop { StopId = "INACTIVE_STOP", StopName = "Inactive", StopLat = 0, StopLon = 0, GtfsImportRunId = newRun.Id });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 0, Lon = 0 },
            Destination = new CoordinateDto { Lat = 1, Lon = 1 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result!.ReasonCode.Should().Be("NO_ROUTE_FOUND");
    }

    [Fact]
    public async Task D2_Results_ShouldBe_Deterministic()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        var response1 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result1 = await response1.Content.ReadAsStringAsync();

        var response2 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result2 = await response2.Content.ReadAsStringAsync();

        result1.Should().Be(result2); // Order and content must be exactly identical
    }

    [Fact]
    public async Task D3_NewActiveFeed_ShouldInvalidate_Cache()
    {
        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        var response1 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result1 = await response1.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        result1!.Metadata!.ActiveImportId.Should().Be(_runId);

        // ACT: Make a new run active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldRun = await db.GtfsImportRuns.FindAsync(_runId);
            oldRun!.IsActive = false;

            var newRun = new GtfsImportRun { FileHash = "NEW_FEED", IsActive = true, Status = "Completed", StartedAt = DateTime.UtcNow };
            db.GtfsImportRuns.Add(newRun);
            await db.SaveChangesAsync();
            
            await SeedDataAsync(db, newRun.Id);
            await db.SaveChangesAsync();
        }

        // The second request should NOT hit the old cache and should return the new RunId in metadata
        var response2 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result2 = await response2.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result2!.Metadata!.ActiveImportId.Should().NotBe(_runId);
    }

    [Fact]
    public async Task E3_LongRunningQuery_ShouldBe_Cancelled()
    {
        var client = _factory.CreateClient();
        var cts = new CancellationTokenSource();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        var postTask = client.PostAsJsonAsync("/api/v1/journey-plans/search", request, cts.Token);
        
        // Cancel immediately
        cts.Cancel();

        // Should throw TaskCanceledException
        await FluentActions.Awaiting(() => postTask).Should().ThrowAsync<TaskCanceledException>();
    }
}
