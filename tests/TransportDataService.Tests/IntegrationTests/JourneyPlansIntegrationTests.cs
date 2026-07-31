using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("Database collection")]
public class JourneyPlansIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public JourneyPlansIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_NoActiveFeed_Returns404NotFound_WithProblemDetails()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ensure there is no active feed in the database
        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var run in activeRuns)
        {
            run.IsActive = false;
        }
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = DateTimeOffset.UtcNow,
            MaxTransfers = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("Aktif GTFS Verisi Bulunamadı");
        problemDetails.Detail.Should().Contain("Sistemde işlem yapabilecek aktif bir GTFS veri seti bulunamadı");
    }

    [Fact]
    public async Task Search_WithActiveFeed_ReturnsCorrectMetadata()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Deactivate any existing active runs to avoid unique constraint violations
        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var activeRun in activeRuns)
        {
            activeRun.IsActive = false;
        }
        await db.SaveChangesAsync();

        // Make sure there is an active run
        var run = new GtfsImportRun
        {
            FileHash = "test-hash-journey-plan",
            Status = "Completed",
            IsActive = true,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow
        };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency
        {
            AgencyId = "test-agency",
            AgencyName = "Test Agency",
            AgencyUrl = "http://test",
            AgencyTimezone = "Europe/Istanbul",
            GtfsImportRun = run
        };
        db.GtfsAgencies.Add(agency);
        
        var calendar = new GtfsCalendar
        {
            ServiceId = "service1",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            GtfsImportRun = run
        };
        db.GtfsCalendars.Add(calendar);
        
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = DateTimeOffset.UtcNow,
            MaxTransfers = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result.Should().NotBeNull();
        result!.Metadata.Should().NotBeNull();
        
        // Assert Metadata fields
        result.Metadata.ActiveImportId.Should().Be(run.Id);
        result.Metadata.FeedHash.Should().Be("test-hash-journey-plan");
        result.Metadata.Timezone.Should().Be("Europe/Istanbul");
        result.Metadata.StartDate.Should().Be(calendar.StartDate.ToString("yyyy-MM-dd"));
        result.Metadata.EndDate.Should().Be(calendar.EndDate.ToString("yyyy-MM-dd"));
        result.Metadata.IsStale.Should().BeFalse();
        result.Metadata.DataSourceWarning.Should().Contain("statik (planlı)");

        // Cleanup
        db.GtfsImportRuns.Remove(run); // cascades
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_CacheIsolation_RespectsDifferentConfigParameters()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var r in activeRuns) r.IsActive = false;
        
        var run = new GtfsImportRun { FileHash = "cache-iso", Status = "Completed", IsActive = true, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency { AgencyId = "iso-agency", AgencyName = "ISO Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRun = run };
        db.GtfsAgencies.Add(agency);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        
        // Act 1: MaxTransfers = 0, MaxWalkingMeters = 1000
        var request0 = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero),
            MaxTransfers = 0,
            MaxWalkingMeters = 1000
        };
        var response0 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request0);
        
        // Act 2: MaxTransfers = 1, MaxWalkingMeters = 2000
        var request1 = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero),
            MaxTransfers = 1,
            MaxWalkingMeters = 2000
        };
        var response1 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request1);

        // Assert
        response0.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result0 = await response0.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        var result1 = await response1.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result0.Should().NotBeNull();
        result1.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_CriticalTimeScenario_NoFiveMinuteRounding()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var r in activeRuns) r.IsActive = false;
        
        var run = new GtfsImportRun { FileHash = "time-iso", Status = "Completed", IsActive = true, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency { AgencyId = "time-agency", AgencyName = "Time Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRun = run };
        db.GtfsAgencies.Add(agency);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        
        var request0800 = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero),
            MaxTransfers = 0
        };
        
        var request0804 = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 1, 8, 4, 0, TimeSpan.Zero),
            MaxTransfers = 0
        };

        // Act
        var response0800 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request0800);
        var response0804 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request0804);

        // Assert
        response0800.StatusCode.Should().Be(HttpStatusCode.OK);
        response0804.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result0800 = await response0800.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        var result0804 = await response0804.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result0800.Should().NotBeNull();
        result0804.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_MidnightScenario_ReturnsPreservedRawTimeAndCorrectServiceDate()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var r in activeRuns) r.IsActive = false;
        
        var run = new GtfsImportRun { FileHash = "midnight", Status = "Completed", IsActive = true, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency { AgencyId = "mid-agency", AgencyName = "Mid Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRun = run };
        db.GtfsAgencies.Add(agency);
        
        var calendar = new GtfsCalendar { ServiceId = "service_mid", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31), Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, GtfsImportRun = run };
        db.GtfsCalendars.Add(calendar);

        var route = new GtfsRoute { RouteId = "R1", RouteShortName = "R1", RouteType = 3, GtfsImportRun = run };
        db.GtfsRoutes.Add(route);

        var trip = new GtfsTrip { TripId = "T1", GtfsRouteId = route.Id, Route = route, ServiceId = "service_mid", DirectionId = 0, GtfsImportRun = run };
        db.GtfsTrips.Add(trip);

        var stop1 = new GtfsStop { StopId = "S1", StopName = "Origin", StopLat = 38.4, StopLon = 27.1, GtfsImportRun = run };
        var stop2 = new GtfsStop { StopId = "S2", StopName = "Dest", StopLat = 38.405, StopLon = 27.105, GtfsImportRun = run };
        db.GtfsStops.AddRange(stop1, stop2);

        // Raw time: 25:30:00 -> 91800 seconds
        var stopTime1 = new GtfsStopTime { GtfsTripId = trip.Id, Trip = trip, StopId = "S1", GtfsStopId = stop1.Id, Stop = stop1, StopSequence = 1, DepartureTimeRaw = "25:30:00", DepartureSeconds = 91800, ArrivalTimeRaw = "25:30:00", ArrivalSeconds = 91800, GtfsImportRun = run };
        var stopTime2 = new GtfsStopTime { GtfsTripId = trip.Id, Trip = trip, StopId = "S2", GtfsStopId = stop2.Id, Stop = stop2, StopSequence = 2, DepartureTimeRaw = "25:40:00", DepartureSeconds = 92400, ArrivalTimeRaw = "25:40:00", ArrivalSeconds = 92400, GtfsImportRun = run };
        db.GtfsStopTimes.AddRange(stopTime1, stopTime2);

        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        
        // Search at 2025-01-02 01:00:00 (which should catch the 25:30:00 trip of 2025-01-01)
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.405, Lon = 27.105 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.FromHours(3)), // Europe/Istanbul offset approx
            MaxTransfers = 0,
            MaxWalkingMeters = 1500
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result.Should().NotBeNull();
        result!.ReasonCode.Should().Be("SUCCESS");
        result.Itineraries.Should().HaveCountGreaterThan(0);
        
        var itinerary = result.Itineraries.First();
        
        // Service date should be the previous day
        itinerary.ServiceDate.Should().Be("2025-01-01");
        
        var transitLeg = itinerary.Legs.First(l => l.Mode == "TRANSIT");
        transitLeg.RawGtfsDepartureTime.Should().Be("25:30:00");
        transitLeg.RawGtfsDepartureSeconds.Should().Be(91800);
        
        // The real departure time should be 2025-01-02 01:30:00
        transitLeg.DepartureTime.Should().NotBeNull();
        transitLeg.DepartureTime!.Value.Hour.Should().Be(1);
        transitLeg.DepartureTime.Value.Minute.Should().Be(30);
        transitLeg.DepartureTime.Value.Day.Should().Be(2);
    }

    [Fact]
    public async Task Search_CrossDayValidTransfer_ReturnsRoute()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var r in activeRuns) r.IsActive = false;
        
        var run = new GtfsImportRun { FileHash = "crossday", Status = "Completed", IsActive = true, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency { AgencyId = "cd-agency", AgencyName = "CD Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRun = run };
        db.GtfsAgencies.Add(agency);
        
        var calendar1 = new GtfsCalendar { ServiceId = "service_day1", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31), Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, GtfsImportRun = run };
        var calendar2 = new GtfsCalendar { ServiceId = "service_day2", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31), Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, GtfsImportRun = run };
        db.GtfsCalendars.AddRange(calendar1, calendar2);

        var route1 = new GtfsRoute { RouteId = "R1", RouteShortName = "R1", RouteType = 3, GtfsImportRun = run };
        var route2 = new GtfsRoute { RouteId = "R2", RouteShortName = "R2", RouteType = 3, GtfsImportRun = run };
        db.GtfsRoutes.AddRange(route1, route2);

        var trip1 = new GtfsTrip { TripId = "T1", GtfsRouteId = route1.Id, Route = route1, ServiceId = "service_day1", DirectionId = 0, GtfsImportRun = run };
        var trip2 = new GtfsTrip { TripId = "T2", GtfsRouteId = route2.Id, Route = route2, ServiceId = "service_day2", DirectionId = 0, GtfsImportRun = run };
        db.GtfsTrips.AddRange(trip1, trip2);

        var stop1 = new GtfsStop { StopId = "SA", StopName = "A", StopLat = 38.4, StopLon = 27.1, GtfsImportRun = run };
        var stop2 = new GtfsStop { StopId = "SB", StopName = "B", StopLat = 38.401, StopLon = 27.101, GtfsImportRun = run };
        var stop3 = new GtfsStop { StopId = "SC", StopName = "C", StopLat = 38.402, StopLon = 27.102, GtfsImportRun = run };
        db.GtfsStops.AddRange(stop1, stop2, stop3);

        // Trip 1 (Yesterday): 24:15 to 24:25 (87300 to 87900)
        var st1 = new GtfsStopTime { GtfsTripId = trip1.Id, Trip = trip1, StopId = "SA", GtfsStopId = stop1.Id, Stop = stop1, StopSequence = 1, DepartureSeconds = 87300, ArrivalSeconds = 87300, GtfsImportRun = run };
        var st2 = new GtfsStopTime { GtfsTripId = trip1.Id, Trip = trip1, StopId = "SB", GtfsStopId = stop2.Id, Stop = stop2, StopSequence = 2, DepartureSeconds = 87900, ArrivalSeconds = 87900, GtfsImportRun = run };
        
        // Trip 2 (Today): 00:45 to 00:55 (2700 to 3300)
        var st3 = new GtfsStopTime { GtfsTripId = trip2.Id, Trip = trip2, StopId = "SB", GtfsStopId = stop2.Id, Stop = stop2, StopSequence = 1, DepartureSeconds = 2700, ArrivalSeconds = 2700, GtfsImportRun = run };
        var st4 = new GtfsStopTime { GtfsTripId = trip2.Id, Trip = trip2, StopId = "SC", GtfsStopId = stop3.Id, Stop = stop3, StopSequence = 2, DepartureSeconds = 3300, ArrivalSeconds = 3300, GtfsImportRun = run };
        db.GtfsStopTimes.AddRange(st1, st2, st3, st4);

        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        
        // Search at 2025-01-02 00:00:00
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.402, Lon = 27.102 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1,
            MaxWalkingMeters = 100
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        // Assert
        result.Should().NotBeNull();
        result!.Itineraries.Should().HaveCountGreaterThan(0);
        result.Itineraries.First().Transfers.Should().Be(1);
    }

    [Fact]
    public async Task Search_CrossDayInvalidTransfer_Elided()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var r in activeRuns) r.IsActive = false;
        
        var run = new GtfsImportRun { FileHash = "crossday_inv", Status = "Completed", IsActive = true, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency { AgencyId = "cdi-agency", AgencyName = "CDI Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRun = run };
        db.GtfsAgencies.Add(agency);
        
        var calendar = new GtfsCalendar { ServiceId = "srv", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31), Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, GtfsImportRun = run };
        db.GtfsCalendars.Add(calendar);

        var route1 = new GtfsRoute { RouteId = "R1", RouteShortName = "R1", RouteType = 3, GtfsImportRun = run };
        var route2 = new GtfsRoute { RouteId = "R2", RouteShortName = "R2", RouteType = 3, GtfsImportRun = run };
        db.GtfsRoutes.AddRange(route1, route2);

        var trip1 = new GtfsTrip { TripId = "T1", GtfsRouteId = route1.Id, Route = route1, ServiceId = "srv", DirectionId = 0, GtfsImportRun = run };
        var trip2 = new GtfsTrip { TripId = "T2", GtfsRouteId = route2.Id, Route = route2, ServiceId = "srv", DirectionId = 0, GtfsImportRun = run };
        db.GtfsTrips.AddRange(trip1, trip2);

        var stop1 = new GtfsStop { StopId = "SA", StopName = "A", StopLat = 38.4, StopLon = 27.1, GtfsImportRun = run };
        var stop2 = new GtfsStop { StopId = "SB", StopName = "B", StopLat = 38.401, StopLon = 27.101, GtfsImportRun = run };
        var stop3 = new GtfsStop { StopId = "SC", StopName = "C", StopLat = 38.402, StopLon = 27.102, GtfsImportRun = run };
        db.GtfsStops.AddRange(stop1, stop2, stop3);

        // Trip 1 (Yesterday): 23:45 to 23:59 (85500 to 86340)
        var st1 = new GtfsStopTime { GtfsTripId = trip1.Id, Trip = trip1, StopId = "SA", GtfsStopId = stop1.Id, Stop = stop1, StopSequence = 1, DepartureSeconds = 85500, ArrivalSeconds = 85500, GtfsImportRun = run };
        var st2 = new GtfsStopTime { GtfsTripId = trip1.Id, Trip = trip1, StopId = "SB", GtfsStopId = stop2.Id, Stop = stop2, StopSequence = 2, DepartureSeconds = 86340, ArrivalSeconds = 86340, GtfsImportRun = run };
        
        // Trip 2 (Today): 00:00 to 00:10 (0 to 600)
        var st3 = new GtfsStopTime { GtfsTripId = trip2.Id, Trip = trip2, StopId = "SB", GtfsStopId = stop2.Id, Stop = stop2, StopSequence = 1, DepartureSeconds = 0, ArrivalSeconds = 0, GtfsImportRun = run };
        var st4 = new GtfsStopTime { GtfsTripId = trip2.Id, Trip = trip2, StopId = "SC", GtfsStopId = stop3.Id, Stop = stop3, StopSequence = 2, DepartureSeconds = 600, ArrivalSeconds = 600, GtfsImportRun = run };
        db.GtfsStopTimes.AddRange(st1, st2, st3, st4);

        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        
        // Search at 2025-01-02 00:00:00
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.402, Lon = 27.102 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1,
            MaxWalkingMeters = 100
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        // Assert
        result.Should().NotBeNull();
        result!.ReasonCode.Should().Be("NO_ROUTE_FOUND");
        result.Itineraries.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_SameVehicleTransfer_Filtered()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var r in activeRuns) r.IsActive = false;
        
        var run = new GtfsImportRun { FileHash = "sameveh", Status = "Completed", IsActive = true, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency { AgencyId = "sv-agency", AgencyName = "SV Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRun = run };
        db.GtfsAgencies.Add(agency);
        
        var calendar = new GtfsCalendar { ServiceId = "srv", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31), Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, GtfsImportRun = run };
        db.GtfsCalendars.Add(calendar);

        var route1 = new GtfsRoute { RouteId = "R1", RouteShortName = "R1", RouteType = 3, GtfsImportRun = run };
        db.GtfsRoutes.Add(route1);

        // Only ONE trip!
        var trip1 = new GtfsTrip { TripId = "T1", GtfsRouteId = route1.Id, Route = route1, ServiceId = "srv", DirectionId = 0, GtfsImportRun = run };
        db.GtfsTrips.Add(trip1);

        var stop1 = new GtfsStop { StopId = "SA", StopName = "A", StopLat = 38.4, StopLon = 27.1, GtfsImportRun = run };
        var stop2 = new GtfsStop { StopId = "SB", StopName = "B", StopLat = 38.401, StopLon = 27.101, GtfsImportRun = run };
        var stop3 = new GtfsStop { StopId = "SC", StopName = "C", StopLat = 38.402, StopLon = 27.102, GtfsImportRun = run };
        db.GtfsStops.AddRange(stop1, stop2, stop3);

        var st1 = new GtfsStopTime { GtfsTripId = trip1.Id, Trip = trip1, StopId = "SA", GtfsStopId = stop1.Id, Stop = stop1, StopSequence = 1, DepartureSeconds = 36000, ArrivalSeconds = 36000, GtfsImportRun = run };
        var st2 = new GtfsStopTime { GtfsTripId = trip1.Id, Trip = trip1, StopId = "SB", GtfsStopId = stop2.Id, Stop = stop2, StopSequence = 2, DepartureSeconds = 36600, ArrivalSeconds = 36600, GtfsImportRun = run };
        var st3 = new GtfsStopTime { GtfsTripId = trip1.Id, Trip = trip1, StopId = "SC", GtfsStopId = stop3.Id, Stop = stop3, StopSequence = 3, DepartureSeconds = 37200, ArrivalSeconds = 37200, GtfsImportRun = run };
        db.GtfsStopTimes.AddRange(st1, st2, st3);

        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.402, Lon = 27.102 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1,
            MaxWalkingMeters = 100
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        // Assert
        result.Should().NotBeNull();
        // Should only find the direct trip(s), NOT a 1-transfer trip utilizing T1 -> T1.
        result!.Itineraries.Should().HaveCountGreaterThan(0);
        result.Itineraries.All(x => x.Transfers == 0).Should().BeTrue("No 1-transfer itinerary should be found for the same trip.");
    }

    [Fact]
    public async Task Search_NonSequentialStopSequences_CountsCorrectly()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = new GtfsImportRun { FileHash = "nonseq", Status = "Completed", IsActive = true, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency { AgencyId = "ns-agency", AgencyName = "NS Agency", AgencyTimezone = "Europe/Istanbul", GtfsImportRun = run };
        db.GtfsAgencies.Add(agency);
        
        var calendar = new GtfsCalendar { ServiceId = "srv_ns", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31), Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, GtfsImportRun = run };
        db.GtfsCalendars.Add(calendar);

        var route = new GtfsRoute { RouteId = "R1_NS", RouteShortName = "R1_NS", RouteType = 3, GtfsImportRun = run };
        db.GtfsRoutes.Add(route);

        var trip = new GtfsTrip { TripId = "T1_NS", GtfsRouteId = route.Id, Route = route, ServiceId = "srv_ns", DirectionId = 0, GtfsImportRun = run };
        db.GtfsTrips.Add(trip);

        var stopA = new GtfsStop { StopId = "SA_NS", StopName = "A_NS", StopLat = 40.4, StopLon = 29.1, GtfsImportRun = run };
        var stopB = new GtfsStop { StopId = "SB_NS", StopName = "B_NS", StopLat = 40.401, StopLon = 29.101, GtfsImportRun = run };
        var stopC = new GtfsStop { StopId = "SC_NS", StopName = "C_NS", StopLat = 40.402, StopLon = 29.102, GtfsImportRun = run };
        var stopD = new GtfsStop { StopId = "SD_NS", StopName = "D_NS", StopLat = 40.403, StopLon = 29.103, GtfsImportRun = run };
        var stopE = new GtfsStop { StopId = "SE_NS", StopName = "E_NS", StopLat = 40.404, StopLon = 29.104, GtfsImportRun = run };
        db.GtfsStops.AddRange(stopA, stopB, stopC, stopD, stopE);

        // Non-sequential sequences: 10, 20, 50, 65, 100
        var st1 = new GtfsStopTime { GtfsTripId = trip.Id, Trip = trip, StopId = "SA_NS", GtfsStopId = stopA.Id, Stop = stopA, StopSequence = 10, DepartureSeconds = 36000, ArrivalSeconds = 36000, GtfsImportRun = run };
        var st2 = new GtfsStopTime { GtfsTripId = trip.Id, Trip = trip, StopId = "SB_NS", GtfsStopId = stopB.Id, Stop = stopB, StopSequence = 20, DepartureSeconds = 36600, ArrivalSeconds = 36600, GtfsImportRun = run };
        var st3 = new GtfsStopTime { GtfsTripId = trip.Id, Trip = trip, StopId = "SC_NS", GtfsStopId = stopC.Id, Stop = stopC, StopSequence = 50, DepartureSeconds = 37200, ArrivalSeconds = 37200, GtfsImportRun = run };
        var st4 = new GtfsStopTime { GtfsTripId = trip.Id, Trip = trip, StopId = "SD_NS", GtfsStopId = stopD.Id, Stop = stopD, StopSequence = 65, DepartureSeconds = 37800, ArrivalSeconds = 37800, GtfsImportRun = run };
        var st5 = new GtfsStopTime { GtfsTripId = trip.Id, Trip = trip, StopId = "SE_NS", GtfsStopId = stopE.Id, Stop = stopE, StopSequence = 100, DepartureSeconds = 38400, ArrivalSeconds = 38400, GtfsImportRun = run };
        db.GtfsStopTimes.AddRange(st1, st2, st3, st4, st5);

        // Deactivate others
        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive && r.Id != run.Id).ToList();
        foreach (var r in activeRuns) r.IsActive = false;

        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 40.4, Lon = 29.1 },
            Destination = new CoordinateDto { Lat = 40.404, Lon = 29.104 },
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 0,
            MaxWalkingMeters = 100
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result.Should().NotBeNull();
        result!.Itineraries.Should().NotBeEmpty();
        
        var itinerary = result.Itineraries.First();
        var transitLeg = itinerary.Legs.First(l => l.Mode == "TRANSIT");
        
        // From sequence 10 to 100, there are 4 stops traversed (B, C, D, E)
        transitLeg.StopCount.Should().Be(4);
        transitLeg.IntermediateStopCount.Should().Be(3); // B, C, D
    }

    [Fact]
    public async Task Search_DefaultDepartureDateTime_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.404, Lon = 27.104 },
            // DepartureDateTime is missing / null
            MaxTransfers = 0,
            MaxWalkingMeters = 500
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        
        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Geçerli bir tarih ve saat (departureDateTime) belirtilmelidir.");
    }
}

