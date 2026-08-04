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

public class TwoTransfersIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TwoTransfersIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                
                var routeProviderDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ulasim_veri_servisi.Services.IWalkingRouteProvider));
                if (routeProviderDescriptor != null) services.Remove(routeProviderDescriptor);
                services.AddSingleton<ulasim_veri_servisi.Services.IWalkingRouteProvider, MockWalkingRouteProvider>();

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase("JourneyPlan_TwoTransfers_TestDb");
                });

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                SeedGtfsData(db);
            });
        });

        _client = _factory.CreateClient();
    }

    private void SeedGtfsData(AppDbContext db)
    {
        if (db.GtfsAgencies.Any()) return;

        // 1. Basic Setup
        var importRun = new GtfsImportRun { StartedAt = DateTime.UtcNow, Status = "Completed", IsActive = true, FileHash = "twotransfershash" };
        db.GtfsImportRuns.Add(importRun);
        db.SaveChanges(); // to get importRun.Id

        var runId = importRun.Id;

        db.GtfsAgencies.Add(new GtfsAgency { AgencyId = "AG1", AgencyName = "Test Agency", AgencyTimezone = "Europe/Istanbul", AgencyUrl = "http://test", GtfsImportRunId = runId });
        db.GtfsCalendars.Add(new GtfsCalendar { ServiceId = "SVC1", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2030, 1, 1), GtfsImportRunId = runId });

        // Stops: 
        // A (Origin) -> B (Transfer 1) -> C (Transfer 2) -> D (Destination)
        var sA = new GtfsStop { StopId = "STOP_A", StopName = "A", StopLat = 41.000, StopLon = 29.000, GtfsImportRunId = runId };
        var sBArr = new GtfsStop { StopId = "STOP_B_ARR", StopName = "B_ARR", StopLat = 41.010, StopLon = 29.010, GtfsImportRunId = runId };
        var sBDep = new GtfsStop { StopId = "STOP_B_DEP", StopName = "B_DEP", StopLat = 41.010, StopLon = 29.011, GtfsImportRunId = runId };
        var sCArr = new GtfsStop { StopId = "STOP_C_ARR", StopName = "C_ARR", StopLat = 41.020, StopLon = 29.020, GtfsImportRunId = runId };
        var sCDep = new GtfsStop { StopId = "STOP_C_DEP", StopName = "C_DEP", StopLat = 41.020, StopLon = 29.021, GtfsImportRunId = runId };
        var sD = new GtfsStop { StopId = "STOP_D", StopName = "D", StopLat = 41.030, StopLon = 29.030, GtfsImportRunId = runId };
        
        var sE = new GtfsStop { StopId = "STOP_E", StopName = "E", StopLat = 41.040, StopLon = 29.040, GtfsImportRunId = runId };
        var sFArr = new GtfsStop { StopId = "STOP_F_ARR", StopName = "F_ARR", StopLat = 41.050, StopLon = 29.050, GtfsImportRunId = runId };
        var sFDep = new GtfsStop { StopId = "STOP_F_DEP", StopName = "F_DEP", StopLat = 41.050, StopLon = 29.051, GtfsImportRunId = runId };
        var sGArr = new GtfsStop { StopId = "STOP_G_ARR", StopName = "G_ARR", StopLat = 41.060, StopLon = 29.060, GtfsImportRunId = runId };
        var sGDep = new GtfsStop { StopId = "STOP_G_DEP", StopName = "G_DEP", StopLat = 41.060, StopLon = 29.061, GtfsImportRunId = runId };
        var sH = new GtfsStop { StopId = "STOP_H", StopName = "H", StopLat = 41.070, StopLon = 29.070, GtfsImportRunId = runId };
        
        db.GtfsStops.AddRange(sA, sBArr, sBDep, sCArr, sCDep, sD, sE, sFArr, sFDep, sGArr, sGDep, sH);

        // Transfers
        db.GtfsTransfers.Add(new GtfsTransfer { FromStopId = "STOP_B_ARR", ToStopId = "STOP_B_DEP", DistanceMeters = 50, WalkingTimeSeconds = 60, GtfsImportRunId = runId });
        db.GtfsTransfers.Add(new GtfsTransfer { FromStopId = "STOP_C_ARR", ToStopId = "STOP_C_DEP", DistanceMeters = 50, WalkingTimeSeconds = 60, GtfsImportRunId = runId });
        db.GtfsTransfers.Add(new GtfsTransfer { FromStopId = "STOP_F_ARR", ToStopId = "STOP_F_DEP", DistanceMeters = 50, WalkingTimeSeconds = 60, GtfsImportRunId = runId });
        db.GtfsTransfers.Add(new GtfsTransfer { FromStopId = "STOP_G_ARR", ToStopId = "STOP_G_DEP", DistanceMeters = 50, WalkingTimeSeconds = 60, GtfsImportRunId = runId });

        // Routes
        var r1 = new GtfsRoute { RouteId = "ROUTE_1", RouteShortName = "R1", RouteType = 3, GtfsImportRunId = runId };
        var r2 = new GtfsRoute { RouteId = "ROUTE_2", RouteShortName = "R2", RouteType = 3, GtfsImportRunId = runId };
        var r3 = new GtfsRoute { RouteId = "ROUTE_3", RouteShortName = "R3", RouteType = 3, GtfsImportRunId = runId };
        var r1Rev = new GtfsRoute { RouteId = "ROUTE_1", RouteShortName = "R1", RouteType = 3, GtfsImportRunId = runId }; // Loop Route
        db.GtfsRoutes.AddRange(r1, r2, r3, r1Rev);

        // Trips
        var t1 = new GtfsTrip { Route = r1, TripId = "TRIP_1", RouteId = "ROUTE_1", ServiceId = "SVC1", DirectionId = 0, GtfsImportRunId = runId };
        var t2 = new GtfsTrip { Route = r2, TripId = "TRIP_2", RouteId = "ROUTE_2", ServiceId = "SVC1", DirectionId = 0, GtfsImportRunId = runId };
        var t3 = new GtfsTrip { Route = r3, TripId = "TRIP_3", RouteId = "ROUTE_3", ServiceId = "SVC1", DirectionId = 0, GtfsImportRunId = runId };
        var t4 = new GtfsTrip { Route = r1Rev, TripId = "TRIP_LOOP", RouteId = "ROUTE_1_REV", ServiceId = "SVC1", DirectionId = 1, GtfsImportRunId = runId };
        
        var t5 = new GtfsTrip { Route = r1, TripId = "TRIP_N1", RouteId = "ROUTE_1", ServiceId = "SVC1", DirectionId = 0, GtfsImportRunId = runId };
        var t6 = new GtfsTrip { Route = r2, TripId = "TRIP_N2", RouteId = "ROUTE_2", ServiceId = "SVC1", DirectionId = 0, GtfsImportRunId = runId };
        var t7 = new GtfsTrip { Route = r3, TripId = "TRIP_N3", RouteId = "ROUTE_3", ServiceId = "SVC1", DirectionId = 0, GtfsImportRunId = runId };
        db.GtfsTrips.AddRange(t1, t2, t3, t4, t5, t6, t7);

        // StopTimes
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t1, Stop = sA, TripId = "TRIP_1", StopId = "STOP_A", StopSequence = 1, DepartureSeconds = 8 * 3600, DepartureTimeRaw = "08:00:00", GtfsImportRunId = runId });
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t1, Stop = sBArr, TripId = "TRIP_1", StopId = "STOP_B_ARR", StopSequence = 2, ArrivalSeconds = 8 * 3600 + 1800, ArrivalTimeRaw = "08:30:00", GtfsImportRunId = runId });

        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t2, Stop = sBDep, TripId = "TRIP_2", StopId = "STOP_B_DEP", StopSequence = 1, DepartureSeconds = 8 * 3600 + 2700, DepartureTimeRaw = "08:45:00", GtfsImportRunId = runId });
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t2, Stop = sCArr, TripId = "TRIP_2", StopId = "STOP_C_ARR", StopSequence = 2, ArrivalSeconds = 9 * 3600 + 900, ArrivalTimeRaw = "09:15:00", GtfsImportRunId = runId });

        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t3, Stop = sCDep, TripId = "TRIP_3", StopId = "STOP_C_DEP", StopSequence = 1, DepartureSeconds = 9 * 3600 + 1800, DepartureTimeRaw = "09:30:00", GtfsImportRunId = runId });
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t3, Stop = sD, TripId = "TRIP_3", StopId = "STOP_D", StopSequence = 2, ArrivalSeconds = 10 * 3600, ArrivalTimeRaw = "10:00:00", GtfsImportRunId = runId });

        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t4, Stop = sBDep, TripId = "TRIP_LOOP", StopId = "STOP_B_DEP", StopSequence = 1, DepartureSeconds = 8 * 3600 + 2000, DepartureTimeRaw = "08:33:20", GtfsImportRunId = runId });
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t4, Stop = sA, TripId = "TRIP_LOOP", StopId = "STOP_A", StopSequence = 2, ArrivalSeconds = 9 * 3600, ArrivalTimeRaw = "09:00:00", GtfsImportRunId = runId });

        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t5, Stop = sE, TripId = "TRIP_N1", StopId = "STOP_E", StopSequence = 1, DepartureSeconds = 23 * 3600 + 1800, DepartureTimeRaw = "23:30:00", GtfsImportRunId = runId });
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t5, Stop = sFArr, TripId = "TRIP_N1", StopId = "STOP_F_ARR", StopSequence = 2, ArrivalSeconds = 23 * 3600 + 2700, ArrivalTimeRaw = "23:45:00", GtfsImportRunId = runId });
        
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t6, Stop = sFDep, TripId = "TRIP_N2", StopId = "STOP_F_DEP", StopSequence = 1, DepartureSeconds = 23 * 3600 + 3300, DepartureTimeRaw = "23:55:00", GtfsImportRunId = runId });
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t6, Stop = sGArr, TripId = "TRIP_N2", StopId = "STOP_G_ARR", StopSequence = 2, ArrivalSeconds = 24 * 3600 + 600, ArrivalTimeRaw = "24:10:00", GtfsImportRunId = runId });
        
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t7, Stop = sGDep, TripId = "TRIP_N3", StopId = "STOP_G_DEP", StopSequence = 1, DepartureSeconds = 24 * 3600 + 1200, DepartureTimeRaw = "24:20:00", GtfsImportRunId = runId });
        db.GtfsStopTimes.Add(new GtfsStopTime { Trip = t7, Stop = sH, TripId = "TRIP_N3", StopId = "STOP_H", StopSequence = 2, ArrivalSeconds = 24 * 3600 + 2400, ArrivalTimeRaw = "24:40:00", GtfsImportRunId = runId });

        db.SaveChanges();
    }

    [Fact]
    public async Task Search_WithMaxTransfers0_ShouldNotFindRouteFromAToD()
    {
        var request = new
        {
            Origin = new { Lat = 41.000, Lon = 29.000 },
            Destination = new { Lat = 41.030, Lon = 29.030 },
            DepartureDateTime = DateTime.UtcNow.Date.AddHours(7).ToString("O"), // 07:00 UTC = 10:00 TRT (or if local, need to handle)
            MaxTransfers = 0,
            MaxWalkingMeters = 1000
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/journey-plans/search", content);
        
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status Code: {response.StatusCode}. Content: {json}");
        using var doc = JsonDocument.Parse(json);
        
        var reasonCode = doc.RootElement.GetProperty("reasonCode").GetString();
        Assert.Equal("NO_ROUTE_FOUND", reasonCode);
    }

    [Fact]
    public async Task Search_WithMaxTransfers2_ShouldFindRouteFromAToD()
    {
        var dt = new DateTime(2026, 1, 1, 7, 50, 0, DateTimeKind.Unspecified); // 07:50
        // Our agency timezone is Europe/Istanbul (+03:00)
        // For testing we just pass it as unspecified or local to the API.

        var request = new
        {
            Origin = new { Lat = 41.000, Lon = 29.000 },
            Destination = new { Lat = 41.030, Lon = 29.030 },
            DepartureDateTime = dt.ToString("s") + "+03:00",
            MaxTransfers = 2,
            MaxWalkingMeters = 1000
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/journey-plans/search", content);
        
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status Code: {response.StatusCode}. Content: {json}");
        using var doc = JsonDocument.Parse(json);
        
        var reasonCode = doc.RootElement.GetProperty("reasonCode").GetString();
        Assert.Equal("SUCCESS", reasonCode);

        var itineraries = doc.RootElement.GetProperty("itineraries").EnumerateArray().ToList();
        Assert.NotEmpty(itineraries);

        var itin = itineraries.First();
        Assert.Equal(2, itin.GetProperty("transferCount").GetInt32());

        // Legs: Walk, Transit1, Walk, Transit2, Walk, Transit3, Walk = 7 legs
        var legs = itin.GetProperty("legs").EnumerateArray().ToList();
        Assert.Equal(7, legs.Count);
        
        Assert.Equal("WALK", legs[0].GetProperty("mode").GetString());
        Assert.Equal("TRANSIT", legs[1].GetProperty("mode").GetString());
        Assert.Equal("WALK", legs[2].GetProperty("mode").GetString());
        Assert.Equal("TRANSIT", legs[3].GetProperty("mode").GetString());
        Assert.Equal("WALK", legs[4].GetProperty("mode").GetString());
        Assert.Equal("TRANSIT", legs[5].GetProperty("mode").GetString());
        Assert.Equal("WALK", legs[6].GetProperty("mode").GetString());

        Assert.Equal("TRIP_1", legs[1].GetProperty("tripId").GetString());
        Assert.Equal("TRIP_2", legs[3].GetProperty("tripId").GetString());
        Assert.Equal("TRIP_3", legs[5].GetProperty("tripId").GetString());
    }

    [Fact]
    public async Task Search_WithMaxTransfers2_CrossDay_ShouldFindRoute()
    {
        var dt = new DateTime(2026, 1, 1, 23, 20, 0, DateTimeKind.Unspecified); // 23:20

        var request = new
        {
            Origin = new { Lat = 41.040, Lon = 29.040 },
            Destination = new { Lat = 41.070, Lon = 29.070 },
            DepartureDateTime = dt.ToString("s") + "+03:00",
            MaxTransfers = 2,
            MaxWalkingMeters = 1000
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/journey-plans/search", content);
        
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status Code: {response.StatusCode}. Content: {json}");
        using var doc = JsonDocument.Parse(json);
        
        var reasonCode = doc.RootElement.GetProperty("reasonCode").GetString();
        Assert.Equal("SUCCESS", reasonCode);

        var itineraries = doc.RootElement.GetProperty("itineraries").EnumerateArray().ToList();
        Assert.NotEmpty(itineraries);

        var itin = itineraries.First();
        var legs = itin.GetProperty("legs").EnumerateArray().ToList();
        Assert.Equal(7, legs.Count);

        // Transit 3 (index 5) should arrive at 00:40 the next day
        var lastTransitLeg = legs[5];
        var arrivalTime = lastTransitLeg.GetProperty("arrivalTime").GetDateTimeOffset();
        
        Assert.Equal("TRIP_N3", lastTransitLeg.GetProperty("tripId").GetString());
        // Check if arrival time crossed midnight
        Assert.Equal(new DateTime(2026, 1, 2).Day, arrivalTime.Day);
        Assert.Equal(0, arrivalTime.Hour);
        Assert.Equal(40, arrivalTime.Minute);
    }
}
