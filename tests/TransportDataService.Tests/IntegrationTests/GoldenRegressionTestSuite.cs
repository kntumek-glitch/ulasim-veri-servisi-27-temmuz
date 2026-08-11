using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;
using Xunit.Abstractions;
namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class GoldenRegressionTestSuite : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public GoldenRegressionTestSuite(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.GtfsImportRuns.ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));

        var run = new GtfsImportRun
        {
            FileHash = "MOCK_IZMIR_HASH",
            Status = "Completed",
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            IsActive = true
        };
        context.GtfsImportRuns.Add(run);
        await context.SaveChangesAsync();

        var runId = run.Id;
        
        var agency = new GtfsAgency { AgencyId = "ESHOT", AgencyName = "ESHOT", AgencyTimezone = "Europe/Istanbul", GtfsImportRunId = runId };
        context.GtfsAgencies.Add(agency);
        
        var sKonak = new GtfsStop { StopId = "S_Konak", StopName = "Konak", StopLat = 38.4192, StopLon = 27.1287, GtfsImportRunId = runId };
        var sAlsancak = new GtfsStop { StopId = "S_Alsancak", StopName = "Alsancak", StopLat = 38.4343, StopLon = 27.1422, GtfsImportRunId = runId };
        var sBuca = new GtfsStop { StopId = "S_Buca", StopName = "Buca", StopLat = 38.3846, StopLon = 27.1687, GtfsImportRunId = runId };
        var sKarsiyaka = new GtfsStop { StopId = "S_Karsiyaka", StopName = "Karsiyaka", StopLat = 38.4552, StopLon = 27.1179, GtfsImportRunId = runId };
        var sBalcova = new GtfsStop { StopId = "S_Balcova", StopName = "Balcova", StopLat = 38.3900, StopLon = 27.0450, GtfsImportRunId = runId };
        var sCigli = new GtfsStop { StopId = "S_Cigli", StopName = "Cigli", StopLat = 38.4891, StopLon = 27.0543, GtfsImportRunId = runId };
        var sGaziemir = new GtfsStop { StopId = "S_Gaziemir", StopName = "Gaziemir", StopLat = 38.3241, StopLon = 27.1328, GtfsImportRunId = runId };
        var sBornova = new GtfsStop { StopId = "S_Bornova", StopName = "Bornova", StopLat = 38.4636, StopLon = 27.2185, GtfsImportRunId = runId };
        var sHalkapinar = new GtfsStop { StopId = "S_Halkapinar", StopName = "Halkapinar", StopLat = 38.4340, StopLon = 27.1700, GtfsImportRunId = runId };
        var sFahrettinAltay = new GtfsStop { StopId = "S_FAltay", StopName = "Fahrettin Altay", StopLat = 38.3950, StopLon = 27.0700, GtfsImportRunId = runId };

        context.GtfsStops.AddRange(sKonak, sAlsancak, sBuca, sKarsiyaka, sBalcova, sCigli, sGaziemir, sBornova, sHalkapinar, sFahrettinAltay);

        var cal = new GtfsCalendar { ServiceId = "SRV_GOLDEN", Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31), GtfsImportRunId = runId };
        context.GtfsCalendars.Add(cal);
        
        var r0 = new GtfsRoute { RouteId = "R_0", RouteShortName = "0 Trans", RouteType = 3, GtfsImportRunId = runId };
        var r1_1 = new GtfsRoute { RouteId = "R_1_1", RouteShortName = "1 Trans A", RouteType = 3, GtfsImportRunId = runId };
        var r1_2 = new GtfsRoute { RouteId = "R_1_2", RouteShortName = "1 Trans B", RouteType = 3, GtfsImportRunId = runId };
        var r2_1 = new GtfsRoute { RouteId = "R_2_1", RouteShortName = "2 Trans A", RouteType = 3, GtfsImportRunId = runId };
        var r2_2 = new GtfsRoute { RouteId = "R_2_2", RouteShortName = "2 Trans B", RouteType = 3, GtfsImportRunId = runId };
        var r2_3 = new GtfsRoute { RouteId = "R_2_3", RouteShortName = "2 Trans C", RouteType = 3, GtfsImportRunId = runId };
        var rArr1 = new GtfsRoute { RouteId = "R_ARR_1", RouteShortName = "ArrBy A", RouteType = 3, GtfsImportRunId = runId };
        var rArr2 = new GtfsRoute { RouteId = "R_ARR_2", RouteShortName = "ArrBy B", RouteType = 3, GtfsImportRunId = runId };
        var rNight = new GtfsRoute { RouteId = "R_NIGHT", RouteShortName = "Night", RouteType = 3, GtfsImportRunId = runId };
        
        context.GtfsRoutes.AddRange(r0, r1_1, r1_2, r2_1, r2_2, r2_3, rArr1, rArr2, rNight);
        
        void AddTrip(GtfsRoute route, string tripId, GtfsStop fromStop, string fromTime, GtfsStop toStop, string toTime)
        {
            var trip = new GtfsTrip { Route = route, TripId = tripId, RouteId = route.RouteId, ServiceId = cal.ServiceId, DirectionId = 0, GtfsImportRunId = runId };
            context.GtfsTrips.Add(trip);
            context.GtfsStopTimes.Add(new GtfsStopTime { Trip = trip, Stop = fromStop, StopId = fromStop.StopId, StopSequence = 1, ArrivalTimeRaw = fromTime, DepartureTimeRaw = fromTime, ArrivalSeconds = ParseTime(fromTime), DepartureSeconds = ParseTime(fromTime), GtfsImportRunId = runId });
            context.GtfsStopTimes.Add(new GtfsStopTime { Trip = trip, Stop = toStop, StopId = toStop.StopId, StopSequence = 2, ArrivalTimeRaw = toTime, DepartureTimeRaw = toTime, ArrivalSeconds = ParseTime(toTime), DepartureSeconds = ParseTime(toTime), GtfsImportRunId = runId });
        }
        
        int ParseTime(string t) { var parts = t.Split(':'); return int.Parse(parts[0])*3600 + int.Parse(parts[1])*60 + int.Parse(parts[2]); }

        AddTrip(r0, "T_0", sKonak, "08:15:00", sAlsancak, "08:30:00");
        AddTrip(r1_1, "T_1_1", sBuca, "08:45:00", sHalkapinar, "09:15:00");
        AddTrip(r1_2, "T_1_2", sHalkapinar, "09:30:00", sKarsiyaka, "10:00:00");
        AddTrip(r2_1, "T_2_1", sBalcova, "09:15:00", sFahrettinAltay, "09:30:00");
        AddTrip(r2_2, "T_2_2", sFahrettinAltay, "09:45:00", sHalkapinar, "10:30:00");
        AddTrip(r2_3, "T_2_3", sHalkapinar, "10:45:00", sCigli, "11:30:00");
        AddTrip(rArr1, "T_ARR_1", sGaziemir, "06:30:00", sHalkapinar, "07:15:00");
        AddTrip(rArr2, "T_ARR_2", sHalkapinar, "07:20:00", sBornova, "07:50:00");
        AddTrip(rNight, "T_NIGHT", sKonak, "24:45:00", sBuca, "25:15:00");

        await context.SaveChangesAsync();
        
        var transferSvc = scope.ServiceProvider.GetRequiredService<IGtfsTransferCalculationService>();
        await transferSvc.CalculateTransfersAsync(runId, CancellationToken.None);
        
        var warmupSvc = scope.ServiceProvider.GetServices<IHostedService>().OfType<SnapshotWarmupService>().Single();
        await warmupSvc.StartAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;


    public static IEnumerable<object[]> GetGoldenScenarios()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../Acceptance/journey-golden-scenarios.json");
        if (!File.Exists(path))
        {
            // Try relative path from root if running from CLI
            path = Path.Combine(Directory.GetCurrentDirectory(), "Acceptance/journey-golden-scenarios.json");
        }
        
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        
        var list = new List<object[]>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            list.Add(new object[] { element.GetProperty("scenario_id").GetString()!, element.GetRawText() });
        }
        return list;
    }

    [Theory]
    [MemberData(nameof(GetGoldenScenarios))]
    public async Task RunGoldenRegressionTest(string scenarioId, string scenarioJson)
    {
        _output.WriteLine($"Executing {scenarioId}");
        
        using var doc = JsonDocument.Parse(scenarioJson);
        var reqNode = doc.RootElement.GetProperty("request");
        var assertions = doc.RootElement.GetProperty("assertions");

        var searchModeStr = reqNode.GetProperty("search_mode").GetString();
        var searchMode = searchModeStr == "ARRIVE_BY" ? RoutingMode.ARRIVE_BY : RoutingMode.DEPART_AT;
        
        var reqDate = DateTime.Parse(reqNode.GetProperty("search_date").GetString()!);
        var reqTimeStr = reqNode.GetProperty("search_time").GetString()!;
        
        // Custom parser to handle >24h (e.g. 24:30:00) which standard TimeSpan.Parse rejects
        var timeParts = reqTimeStr.Split(':');
        var reqTime = new TimeSpan(int.Parse(timeParts[0]), int.Parse(timeParts[1]), int.Parse(timeParts[2]));
        
        var dateTime = reqDate.Add(reqTime);
        
        var request = new JourneyPlanV2SearchRequest
        {
            Origin = new CoordinateDto 
            { 
                Lat = reqNode.GetProperty("origin").GetProperty("lat").GetDouble(), 
                Lon = reqNode.GetProperty("origin").GetProperty("lon").GetDouble() 
            },
            Destination = new CoordinateDto 
            { 
                Lat = reqNode.GetProperty("destination").GetProperty("lat").GetDouble(), 
                Lon = reqNode.GetProperty("destination").GetProperty("lon").GetDouble() 
            },
            DateTime = dateTime,
            SearchMode = searchMode,
            MaxWalkingMeters = reqNode.TryGetProperty("max_walking_meters", out var mw) ? mw.GetInt32() : 2000,
            MaxTransfers = reqNode.TryGetProperty("max_transfers", out var mt) ? mt.GetInt32() : 1,
            IncludeWalkingGeometry = false
        };

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v2/journey-plans/search", request);
        
        var isRouteFoundExpected = assertions.GetProperty("is_route_found").GetBoolean();
        
        if (!isRouteFoundExpected)
        {
            if (response.IsSuccessStatusCode)
            {
                var searchResultNoRoute = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
                searchResultNoRoute?.Itineraries.Should().BeEmpty("Expected no route to be found.");
            }
            return;
        }

        response.EnsureSuccessStatusCode();
        var searchResult = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        searchResult.Should().NotBeNull();
        searchResult!.Itineraries.Should().NotBeEmpty("Route was expected to be found.");
        
        var bestItinerary = searchResult.Itineraries.First();
        
        // Semantic Assertions
        
        // 1. Max Transfers
        if (assertions.TryGetProperty("max_allowed_transfers", out var maxT) && maxT.ValueKind != JsonValueKind.Null)
        {
            var expectedTransfers = maxT.GetInt32();
            bestItinerary.TransferCount.Should().BeLessThanOrEqualTo(expectedTransfers);
        }
        
        // 2. Latest Arrival Time
        if (assertions.TryGetProperty("latest_arrival_time", out var lat) && lat.ValueKind != JsonValueKind.Null)
        {
            var expectedArrStr = lat.GetString()!;
            var arrTimeParts = expectedArrStr.Split(':');
            var expectedArrTimeSpan = new TimeSpan(int.Parse(arrTimeParts[0]), int.Parse(arrTimeParts[1]), int.Parse(arrTimeParts[2]));
            var expectedArrDate = reqDate.Add(expectedArrTimeSpan);
            
            bestItinerary.ArrivalTime.Should().BeOnOrBefore(expectedArrDate);
        }
        
        // 3. Min Departure Time (for ARRIVE_BY)
        if (assertions.TryGetProperty("min_departure_time_for_arrive_by", out var minDep) && minDep.ValueKind != JsonValueKind.Null)
        {
            var expectedDepStr = minDep.GetString()!;
            var depTimeParts = expectedDepStr.Split(':');
            var expectedDepTimeSpan = new TimeSpan(int.Parse(depTimeParts[0]), int.Parse(depTimeParts[1]), int.Parse(depTimeParts[2]));
            var expectedDepDate = reqDate.Add(expectedDepTimeSpan);
            
            bestItinerary.DepartureTime.Should().BeOnOrAfter(expectedDepDate);
        }
    }
}
