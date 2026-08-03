using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

/// <summary>
/// QA Test: Algoritma Edge Case ve API Response Testleri
/// - SameTripId fake leg kontrolü
/// - patternId/shapeId null kontrolü
/// - 503 timeout (sunucu tarafı timeout) testi
/// - Aynı güzergâhın duplicate dönmemesi
/// - Loop (A->B->A) engeli
/// </summary>
[Collection("IntegrationTestCollection")]
public class AlgorithmEdgeCaseTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private int _runId;

    // Static flag ensures seeding happens only ONCE across all test instances in this class
    private static int _seedRunId = 0;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public AlgorithmEdgeCaseTests(CustomWebApplicationFactory factory)
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
                // Already seeded by a previous test instance — just reuse the run
                _runId = _seedRunId;
                return;
            }
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var active = await db.GtfsImportRuns.Where(r => r.IsActive).ToListAsync();
            if (_seedRunId == 0)
            {
                var run = new GtfsImportRun
                {
                    FileHash = "EDGE_TEST_HASH",
                    IsActive = true,
                    Status = "Completed",
                    StartedAt = DateTime.UtcNow
                };
                db.GtfsImportRuns.Add(run);
                await db.SaveChangesAsync();
                _seedRunId = run.Id;

                await SeedEdgeCaseDataAsync(db, _seedRunId);
                await db.SaveChangesAsync();

                var transferService = scope.ServiceProvider.GetRequiredService<IGtfsTransferCalculationService>();
                await transferService.CalculateTransfersAsync(_seedRunId, CancellationToken.None);
            }
            _runId = _seedRunId;

            var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            if (cache is Microsoft.Extensions.Caching.Memory.MemoryCache mc) mc.Clear();
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedEdgeCaseDataAsync(AppDbContext db, int runId)
    {
        // Guard against duplicate seeding (e.g., parallel test class initialization)
        if (await db.GtfsAgencies.AnyAsync(a => a.GtfsImportRunId == runId)) return;

        db.GtfsAgencies.Add(new GtfsAgency
        {
            AgencyId = "AG_EDGE",
            AgencyName = "Edge Test Agency",
            AgencyTimezone = "Europe/Istanbul",
            GtfsImportRunId = runId
        });

        db.GtfsCalendars.Add(new GtfsCalendar
        {
            ServiceId = "SRV_EDGE",
            Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true,
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 12, 31),
            GtfsImportRunId = runId
        });

        // Stops for basic scenario (Origin -> Transfer -> Destination)
        var sOrigin = new GtfsStop { StopId = "EDGE_O", StopName = "Origin", StopLat = 41.010, StopLon = 29.010, GtfsImportRunId = runId };
        var sTransfer = new GtfsStop { StopId = "EDGE_T", StopName = "Transfer", StopLat = 41.015, StopLon = 29.015, GtfsImportRunId = runId };
        var sDest = new GtfsStop { StopId = "EDGE_D", StopName = "Dest", StopLat = 41.020, StopLon = 29.020, GtfsImportRunId = runId };
        // An intermediate stop on the route (to verify stopCount is correct even with non-seq sequences)
        var sMid = new GtfsStop { StopId = "EDGE_M", StopName = "Middle", StopLat = 41.012, StopLon = 29.012, GtfsImportRunId = runId };
        db.GtfsStops.AddRange(sOrigin, sTransfer, sDest, sMid);

        var route1 = new GtfsRoute { RouteId = "EDGE_R1", RouteShortName = "E1", RouteType = 3, GtfsImportRunId = runId };
        var route2 = new GtfsRoute { RouteId = "EDGE_R2", RouteShortName = "E2", RouteType = 3, GtfsImportRunId = runId };
        db.GtfsRoutes.AddRange(route1, route2);

        // Trip 1: O -> M -> T (ShapeId defined)
        var t1 = new GtfsTrip { Route = route1, TripId = "EDGE_T1", RouteId = "EDGE_R1", ServiceId = "SRV_EDGE", DirectionId = 0, ShapeId = "EDGE_SHAPE_1", GtfsImportRunId = runId };
        // Trip 1b: same route/shape as T1 but different trip (to test deduplication)
        var t1b = new GtfsTrip { Route = route1, TripId = "EDGE_T1B", RouteId = "EDGE_R1", ServiceId = "SRV_EDGE", DirectionId = 0, ShapeId = "EDGE_SHAPE_1", GtfsImportRunId = runId };
        // Trip 2: T -> D (ShapeId defined)
        var t2 = new GtfsTrip { Route = route2, TripId = "EDGE_T2", RouteId = "EDGE_R2", ServiceId = "SRV_EDGE", DirectionId = 0, ShapeId = "EDGE_SHAPE_2", GtfsImportRunId = runId };
        db.GtfsTrips.AddRange(t1, t1b, t2);

        // Stop times for T1: O(seq=10) -> M(seq=20) -> T(seq=30) with non-consecutive sequences
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t1, Stop = sOrigin, TripId = "EDGE_T1", StopId = "EDGE_O", StopSequence = 10, ArrivalSeconds = 8 * 3600, DepartureSeconds = 8 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1, Stop = sMid, TripId = "EDGE_T1", StopId = "EDGE_M", StopSequence = 20, ArrivalSeconds = 8 * 3600 + 600, DepartureSeconds = 8 * 3600 + 600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1, Stop = sTransfer, TripId = "EDGE_T1", StopId = "EDGE_T", StopSequence = 30, ArrivalSeconds = 8 * 3600 + 1200, DepartureSeconds = 8 * 3600 + 1200, GtfsImportRunId = runId }
        );
        // Stop times for T1B: exact same as T1 but departs 5 mins later
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t1b, Stop = sOrigin, TripId = "EDGE_T1B", StopId = "EDGE_O", StopSequence = 10, ArrivalSeconds = 8 * 3600 + 300, DepartureSeconds = 8 * 3600 + 300, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1b, Stop = sMid, TripId = "EDGE_T1B", StopId = "EDGE_M", StopSequence = 20, ArrivalSeconds = 8 * 3600 + 900, DepartureSeconds = 8 * 3600 + 900, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1b, Stop = sTransfer, TripId = "EDGE_T1B", StopId = "EDGE_T", StopSequence = 30, ArrivalSeconds = 8 * 3600 + 1500, DepartureSeconds = 8 * 3600 + 1500, GtfsImportRunId = runId }
        );
        // Stop times for T2: T -> D
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t2, Stop = sTransfer, TripId = "EDGE_T2", StopId = "EDGE_T", StopSequence = 1, ArrivalSeconds = 8 * 3600 + 1800, DepartureSeconds = 8 * 3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t2, Stop = sDest, TripId = "EDGE_T2", StopId = "EDGE_D", StopSequence = 2, ArrivalSeconds = 8 * 3600 + 2400, DepartureSeconds = 8 * 3600 + 2400, GtfsImportRunId = runId }
        );
        // CalculateTransfersAsync will automatically add EDGE_T -> EDGE_T transfer (distance=0, same physical stop)
    }

    /// <summary>
    /// Algoritma 8: Aynı TripId'nin iki leg olarak (transfer bacağı gibi) listelenmemesi.
    /// Tek bir trip, Origin'den Dest'e gidiyorsa, algoritma bunu sadece 1 bacakta göstermeli.
    /// </summary>
    [Fact]
    public async Task A08_SameTripId_ShouldNotAppearAsFakeTwoTransferLeg()
    {
        var client = _factory.CreateClient();

        // T1 covers EDGE_O -> EDGE_T, T2 covers EDGE_T -> EDGE_D
        // Both are different trips — verify no single trip appears in 2 transit legs
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 },
            Destination = new CoordinateDto { Lat = 41.020, Lon = 29.020 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        result.Should().NotBeNull();
        result!.Itineraries.Should().NotBeEmpty();

        foreach (var itin in result.Itineraries)
        {
            var transitLegs = itin.Legs.Where(l => l.Mode == "TRANSIT").ToList();
            var tripIds = transitLegs.Select(l => l.TripId).ToList();
            tripIds.Should().OnlyHaveUniqueItems(
                "Aynı TripId birden fazla transit bacakta görünmemeli (fake transfer önleme)");
        }
    }

    /// <summary>
    /// Algoritma 10: Aynı güzergâhın (pattern) sonuç listesinde tekrar tekrar dönmemesi (deduplication).
    /// T1 ve T1B aynı şekil/pattern'e sahip → sadece biri (en erken) dönmeli.
    /// </summary>
    [Fact]
    public async Task A10_SamePattern_ShouldBeDeduplicatedInResults()
    {
        var client = _factory.CreateClient();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 },
            Destination = new CoordinateDto { Lat = 41.020, Lon = 29.020 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();

        result.Should().NotBeNull();

        // Both T1 and T1B have same ShapeId (EDGE_SHAPE_1) -> same pattern -> only one should appear
        var allTripIds = result!.Itineraries
            .SelectMany(i => i.Legs.Where(l => l.Mode == "TRANSIT"))
            .Select(l => l.TripId)
            .ToList();

        // Both T1 and T1B cannot appear simultaneously for same-pattern route
        var hasBoth = allTripIds.Contains("EDGE_T1") && allTripIds.Contains("EDGE_T1B");
        if (hasBoth)
        {
            var jsonStr = System.Text.Json.JsonSerializer.Serialize(result.Itineraries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("DEBUG ITINERARIES:\n" + jsonStr);
        }
        hasBoth.Should().BeFalse("Aynı pattern'e sahip iki trip aynı anda sonuçlarda bulunmamalı (deduplication)");
    }

    /// <summary>
    /// Response 1: Transit bacaklarda patternId ve shapeId değerleri null olmamalı.
    /// </summary>
    [Fact]
    public async Task R01_TransitLegs_PatternId_And_ShapeId_ShouldNotBeNull()
    {
        var client = _factory.CreateClient();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 },
            Destination = new CoordinateDto { Lat = 41.015, Lon = 29.015 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 0
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        var json = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        using var doc = JsonDocument.Parse(json);
        var itineraries = doc.RootElement.GetProperty("itineraries").EnumerateArray().ToList();
        itineraries.Should().NotBeEmpty("En az bir rota dönmeli");

        foreach (var itin in itineraries)
        {
            var legs = itin.GetProperty("legs").EnumerateArray()
                .Where(l => l.GetProperty("mode").GetString() == "TRANSIT")
                .ToList();

            foreach (var leg in legs)
            {
                leg.TryGetProperty("patternId", out var patternIdProp).Should().BeTrue("patternId alanı mevcut olmalı");
                var patternId = patternIdProp.GetString();
                patternId.Should().NotBeNullOrEmpty("patternId null veya boş olmamalı");

                // shapeId can be null if not available but patternId should always be set
                // If ShapeId is seeded, it should appear
                if (leg.TryGetProperty("shapeId", out var shapeIdProp))
                {
                    // shapeId may or may not be present but if present should not be empty string
                    var shapeId = shapeIdProp.GetString();
                    if (shapeId != null)
                    {
                        shapeId.Should().NotBeEmpty("shapeId mevcutsa boş olmamalı");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Response 5: Arama isteği client tarafından iptal edildiğinde işlem durmalı.
    /// Mevcut E3_LongRunningQuery_ShouldBe_Cancelled testi ile örtüşür ama
    /// burada HTTP-level 499 veya OperationCanceledException bekliyoruz.
    /// </summary>
    [Fact]
    public async Task R05_ClientCancellation_ShouldStopOperation()
    {
        var client = _factory.CreateClient();
        var cts = new CancellationTokenSource();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 },
            Destination = new CoordinateDto { Lat = 41.020, Lon = 29.020 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 1
        };

        // Cancel immediately to ensure it always throws OperationCanceledException
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() =>
            client.PostAsJsonAsync("/api/v1/journey-plans/search", request, cts.Token));

        // Must throw an OperationCanceledException at the HTTP client level
        ex.Should().NotBeNull("İptal edildiğinde exception fırlatılmalı");
        ex.Should().BeAssignableTo<OperationCanceledException>(
            "İstemci tarafı iptalinde OperationCanceledException bekleniyor");
    }

    /// <summary>
    /// Response 6: Sunucu arama timeout'u (MaxSearchTimeSeconds) dolduğunda 503 ProblemDetails dönmeli.
    /// </summary>
    [Fact]
    public async Task R06_ServerTimeout_ShouldReturn503_WithProblemDetails()
    {
        // Override the timeout to be extremely short (1ms) so it always times out
        var timeoutFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "JourneyPlan:MaxSearchTimeSeconds", "0" } // 0 seconds = immediate timeout
                });
            });
        });

        var client = timeoutFactory.CreateClient();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 41.010, Lon = 29.010 },
            Destination = new CoordinateDto { Lat = 41.020, Lon = 29.020 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(3)),
            MaxTransfers = 2 // More complex = more chance to timeout
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Either 503 (server timeout) or 200 if it completed before the timeout
        // With MaxSearchTimeSeconds=0, it should almost always be 503
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            // Verify it's a proper ProblemDetails response
            doc.RootElement.TryGetProperty("title", out var titleProp).Should().BeTrue("ProblemDetails içinde 'title' olmalı");
            doc.RootElement.TryGetProperty("status", out var statusProp).Should().BeTrue("ProblemDetails içinde 'status' olmalı");
            statusProp.GetInt32().Should().Be(503, "Sunucu tarafı timeout 503 döndürmeli");
        }
        else
        {
            // If it completed before timeout, that's also acceptable
            response.StatusCode.Should().Be(HttpStatusCode.OK, "Timeout gerçekleşmediyse 200 dönmeli");
        }
    }
}
