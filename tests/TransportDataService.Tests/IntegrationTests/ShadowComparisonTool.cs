using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using TransportDataService.Models.Gtfs.JourneyPlan;
using Xunit;
using Xunit.Abstractions;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class ShadowComparisonTool : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public ShadowComparisonTool(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task RunShadowComparison()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../Acceptance/journey-golden-scenarios.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "Acceptance/journey-golden-scenarios.json");
        }
        
        var json = await File.ReadAllTextAsync(path);
        var doc = JsonDocument.Parse(json);
        var client = _factory.CreateClient();
        
        var reportItems = new List<object>();

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var scenarioId = element.GetProperty("scenario_id").GetString()!;
            var reqNode = element.GetProperty("request");
            
            var searchModeStr = reqNode.GetProperty("search_mode").GetString()!;
            var isArriveBy = searchModeStr == "ARRIVE_BY";
            var searchMode = isArriveBy ? RoutingMode.ARRIVE_BY : RoutingMode.DEPART_AT;
            
            var reqDate = DateTime.Parse(reqNode.GetProperty("search_date").GetString()!);
            var searchTimeStr = reqNode.GetProperty("search_time").GetString()!;
            var parts = searchTimeStr.Split(':');
            var reqTime = new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            var dateTime = reqDate.Add(reqTime);
            
            var maxWalking = reqNode.TryGetProperty("max_walking_meters", out var mw) ? mw.GetInt32() : 2000;
            
            var latO = reqNode.GetProperty("origin").GetProperty("lat").GetDouble();
            var lonO = reqNode.GetProperty("origin").GetProperty("lon").GetDouble();
            var latD = reqNode.GetProperty("destination").GetProperty("lat").GetDouble();
            var lonD = reqNode.GetProperty("destination").GetProperty("lon").GetDouble();

            // V1 Request
            var reqV1 = new JourneyPlanSearchRequest
            {
                Origin = new CoordinateDto { Lat = latO, Lon = lonO },
                Destination = new CoordinateDto { Lat = latD, Lon = lonD },
                DepartureDateTime = dateTime,
                MaxTransfers = 2,
                MaxWalkingMeters = maxWalking,
                MaxResults = 5,
                IncludeIntermediateStops = false
            };
            
            // V2 Request
            var reqV2 = new JourneyPlanV2SearchRequest
            {
                Origin = new CoordinateDto { Lat = latO, Lon = lonO },
                Destination = new CoordinateDto { Lat = latD, Lon = lonD },
                DateTime = dateTime,
                SearchMode = searchMode,
                MaxTransfers = 2,
                MaxWalkingMeters = maxWalking,
                MaxResults = 5,
                IncludeIntermediateStops = false,
                IncludeWalkingGeometry = false
            };

            // Run V1
            JourneyPlanSearchResponse? resV1 = null;
            long v1CalcMs = 0;
            if (!isArriveBy)
            {
                var swV1 = Stopwatch.StartNew();
                var httpV1 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", reqV1);
                swV1.Stop();
                v1CalcMs = swV1.ElapsedMilliseconds;
                if (httpV1.IsSuccessStatusCode)
                {
                    resV1 = await httpV1.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
                }
            }

            // Run V2
            var swV2 = Stopwatch.StartNew();
            var httpV2 = await client.PostAsJsonAsync("/api/v2/journey-plans/search", reqV2);
            swV2.Stop();
            long v2CalcMs = swV2.ElapsedMilliseconds;
            JourneyPlanSearchResponse? resV2 = null;
            if (httpV2.IsSuccessStatusCode)
            {
                resV2 = await httpV2.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
            }

            // Extract metrics
            var it1 = resV1?.Itineraries?.FirstOrDefault();
            var it2 = resV2?.Itineraries?.FirstOrDefault();

            int v1ResultCount = resV1?.Itineraries?.Count ?? 0;
            int v2ResultCount = resV2?.Itineraries?.Count ?? 0;
            
            DateTimeOffset? v1Arr = it1?.ArrivalTime;
            DateTimeOffset? v2Arr = it2?.ArrivalTime;

            int? v1Transfer = it1?.TransferCount;
            int? v2Transfer = it2?.TransferCount;

            int? v1Walking = it1?.TotalWalkingTimeSeconds;
            int? v2Walking = it2?.TotalWalkingTimeSeconds;
            
            bool sameTopology = false;
            int arrivalDiff = 0;

            if (isArriveBy)
            {
                // Defined by user instructions: ARRIVE_BY returns 400 in V1, resulting in same_topology = false
                sameTopology = false;
            }
            else if (v1ResultCount == 0 && v2ResultCount == 0)
            {
                sameTopology = true; // Both found no route
            }
            else if (v1ResultCount > 0 && v2ResultCount > 0 && it1 != null && it2 != null)
            {
                sameTopology = (it1.TransferCount == it2.TransferCount) && 
                               (Math.Abs(it1.TotalWalkingTimeSeconds - it2.TotalWalkingTimeSeconds) < 60); // Roughly same
                arrivalDiff = (int)(v1Arr!.Value - v2Arr!.Value).TotalSeconds;
            }
            else 
            {
                sameTopology = false;
            }

            var item = new
            {
                scenario_id = scenarioId,
                v1_result_count = v1ResultCount,
                v2_result_count = v2ResultCount,
                v1_top1_arrival = v1Arr?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                v2_top1_arrival = v2Arr?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                v1_transfer_count = v1Transfer,
                v2_transfer_count = v2Transfer,
                v1_walking_time = v1Walking,
                v2_walking_time = v2Walking,
                v1_calc_ms = v1CalcMs,
                v2_calc_ms = v2CalcMs,
                same_topology = sameTopology,
                arrival_difference_seconds = arrivalDiff
            };
            
            reportItems.Add(item);
        }

        // Write comparison output
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "shadow-comparison-results.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(reportItems, options));
        _output.WriteLine($"Saved shadow comparison to {outputPath}");
    }
}
