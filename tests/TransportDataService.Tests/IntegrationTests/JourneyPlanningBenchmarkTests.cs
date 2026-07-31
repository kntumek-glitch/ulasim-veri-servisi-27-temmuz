using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TransportDataService;
using TransportDataService.Models.Gtfs.JourneyPlan;
using Xunit;
using Xunit.Abstractions;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class JourneyPlanningBenchmarkTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public JourneyPlanningBenchmarkTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task RunBenchmarks()
    {
        var client = _factory.CreateClient();

        // 1. DIRECT ROUTE
        var requestDirect = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 }, // Stop A
            Destination = new CoordinateDto { Lat = 38.402, Lon = 27.102 }, // Stop C
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 0,
            MaxWalkingMeters = 1500
        };

        // Warmup
        await client.PostAsJsonAsync("/api/v1/journey-plans/search", requestDirect);

        var sw = Stopwatch.StartNew();
        var memBefore = GC.GetTotalAllocatedBytes(true);
        var response1 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", requestDirect);
        var memAfter = GC.GetTotalAllocatedBytes(true);
        sw.Stop();

        _output.WriteLine($"[DIRECT] Time: {sw.ElapsedMilliseconds} ms, Memory: {(memAfter - memBefore) / 1024.0:F2} KB");

        // 2. ONE TRANSFER ROUTE
        var requestTransfer = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 }, // Stop A
            Destination = new CoordinateDto { Lat = 40.0, Lon = 29.0 }, // Far away stop
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1,
            MaxWalkingMeters = 1500
        };

        sw.Restart();
        memBefore = GC.GetTotalAllocatedBytes(true);
        var response2 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", requestTransfer);
        memAfter = GC.GetTotalAllocatedBytes(true);
        sw.Stop();

        _output.WriteLine($"[1-TRANSFER] Time: {sw.ElapsedMilliseconds} ms, Memory: {(memAfter - memBefore) / 1024.0:F2} KB");

        // 3. NOT FOUND ROUTE
        var requestNotFound = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 42.0, Lon = 35.0 }, // Nowhere
            DepartureDateTime = new DateTimeOffset(2025, 1, 2, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1,
            MaxWalkingMeters = 1500
        };

        sw.Restart();
        memBefore = GC.GetTotalAllocatedBytes(true);
        var response3 = await client.PostAsJsonAsync("/api/v1/journey-plans/search", requestNotFound);
        memAfter = GC.GetTotalAllocatedBytes(true);
        sw.Stop();

        _output.WriteLine($"[NOT FOUND] Time: {sw.ElapsedMilliseconds} ms, Memory: {(memAfter - memBefore) / 1024.0:F2} KB");
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _output.WriteLine($"DB Stats -> Stops: {db.GtfsStops.Count()}, Trips: {db.GtfsTrips.Count()}, StopTimes: {db.GtfsStopTimes.Count()}");
    }
}
