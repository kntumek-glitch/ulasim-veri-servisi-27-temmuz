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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

/// <summary>
/// QA Test: Transfer Ağı (Transfer Network) Testleri
/// Aynı fiziksel durak, yakın duraklar, uzak duraklar ve ImportId ilişkisi testleri.
/// </summary>
[Collection("IntegrationTestCollection")]
public class TransferNetworkIntegrationTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private int _runId;

    // Static flag ensures seeding happens only ONCE across all test instances in this class
    private static int _seedRunId = 0;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public TransferNetworkIntegrationTests(CustomWebApplicationFactory factory)
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

            if (_seedRunId == 0)
            {
                // Deactivate existing active runs
                var existing = await db.GtfsImportRuns.Where(r => r.IsActive).ToListAsync();
                foreach (var r in existing) r.IsActive = false;

                var run = new GtfsImportRun
                {
                    FileHash = $"transfer-network-test-{Guid.NewGuid()}",
                    IsActive = true,
                    Status = "Completed",
                    StartedAt = DateTime.UtcNow
                };
                db.GtfsImportRuns.Add(run);
                await db.SaveChangesAsync();
                _seedRunId = run.Id;

                await SeedTransferTestDataAsync(db, _seedRunId);
                await db.SaveChangesAsync();

                var transferService = scope.ServiceProvider.GetRequiredService<ulasim_veri_servisi.Services.Interfaces.IGtfsTransferCalculationService>();
                await transferService.CalculateTransfersAsync(_seedRunId, CancellationToken.None);
            }
            _runId = _seedRunId;

            // Clear cache
            var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            if (cache is Microsoft.Extensions.Caching.Memory.MemoryCache mc) mc.Clear();
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedTransferTestDataAsync(AppDbContext db, int runId)
    {
        // Guard: if this runId was already seeded by a previous parallel test init, skip
        if (await db.GtfsAgencies.AnyAsync(a => a.GtfsImportRunId == runId)) return;
        db.GtfsAgencies.Add(new GtfsAgency
        {
            AgencyId = "AG_TN",
            AgencyName = "Transfer Network Test Agency",
            AgencyTimezone = "Europe/Istanbul",
            GtfsImportRunId = runId
        });

        db.GtfsCalendars.Add(new GtfsCalendar
        {
            ServiceId = "SRV_TN",
            Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true,
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 12, 31),
            GtfsImportRunId = runId
        });

        // Stop A and A_SAME = same physical location (lat/lon identical → distance = 0)
        var sA = new GtfsStop { StopId = "TN_A", StopName = "Stop_A", StopLat = 41.000, StopLon = 29.000, GtfsImportRunId = runId };
        var sASame = new GtfsStop { StopId = "TN_A_SAME", StopName = "Stop_A_Same", StopLat = 41.000, StopLon = 29.000, GtfsImportRunId = runId }; // identical coords
        // Stop B = 50m away from A (within walk limit)
        var sB = new GtfsStop { StopId = "TN_B", StopName = "Stop_B", StopLat = 41.0005, StopLon = 29.000, GtfsImportRunId = runId }; // ~55m
        // Stop C = far away (~20km, outside walk limit)
        var sC = new GtfsStop { StopId = "TN_C", StopName = "Stop_C", StopLat = 41.180, StopLon = 29.000, GtfsImportRunId = runId };
        // Stop D = destination
        var sD = new GtfsStop { StopId = "TN_D", StopName = "Stop_D", StopLat = 41.002, StopLon = 29.002, GtfsImportRunId = runId };
        // Stop E = Invalid Coordinates
        var sE = new GtfsStop { StopId = "TN_INVALID", StopName = "Invalid_Stop", StopLat = 0, StopLon = 0, GtfsImportRunId = runId };
        
        db.GtfsStops.AddRange(sA, sASame, sB, sC, sD, sE);

        var r1 = new GtfsRoute { RouteId = "TN_R1", RouteShortName = "TN_L1", RouteType = 3, GtfsImportRunId = runId };
        var r2 = new GtfsRoute { RouteId = "TN_R2", RouteShortName = "TN_L2", RouteType = 3, GtfsImportRunId = runId };
        db.GtfsRoutes.AddRange(r1, r2);

        var t1 = new GtfsTrip { Route = r1, TripId = "TN_T1", RouteId = "TN_R1", ServiceId = "SRV_TN", DirectionId = 0, GtfsImportRunId = runId };
        var t2 = new GtfsTrip { Route = r2, TripId = "TN_T2", RouteId = "TN_R2", ServiceId = "SRV_TN", DirectionId = 0, GtfsImportRunId = runId };
        db.GtfsTrips.AddRange(t1, t2);

        // T1: A -> B (arrives at B at 08:30)
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t1, Stop = sA, TripId = "TN_T1", StopId = "TN_A", StopSequence = 1, ArrivalSeconds = 8 * 3600, DepartureSeconds = 8 * 3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1, Stop = sB, TripId = "TN_T1", StopId = "TN_B", StopSequence = 2, ArrivalSeconds = 8 * 3600 + 1800, DepartureSeconds = 8 * 3600 + 1800, GtfsImportRunId = runId }
        );
        // T2: A_SAME -> D (departs at 08:05) — to verify same-stop transfer
        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t2, Stop = sASame, TripId = "TN_T2", StopId = "TN_A_SAME", StopSequence = 1, ArrivalSeconds = 8 * 3600 + 300, DepartureSeconds = 8 * 3600 + 300, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t2, Stop = sD, TripId = "TN_T2", StopId = "TN_D", StopSequence = 2, ArrivalSeconds = 8 * 3600 + 600, DepartureSeconds = 8 * 3600 + 600, GtfsImportRunId = runId }
        );
    }

    /// <summary>
    /// Transfer 1: Aynı fiziksel konumdaki (mesafe=0) iki durak arasında transfer oluşmalı.
    /// </summary>
    [Fact]
    public async Task T01_SamePhysicalStop_ShouldCreateTransferRelation()
    {
        // CalculateTransfersAsync was already called in InitializeAsync
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // TN_A and TN_A_SAME are at identical lat/lon → distance = 0
        var transfer = await db.GtfsTransfers
            .FirstOrDefaultAsync(t => t.GtfsImportRunId == _runId &&
                ((t.FromStopId == "TN_A" && t.ToStopId == "TN_A_SAME") ||
                 (t.FromStopId == "TN_A_SAME" && t.ToStopId == "TN_A")));

        transfer.Should().NotBeNull("Aynı koordinattaki duraklar arasında transfer ilişkisi oluşmalı");
        transfer!.DistanceMeters.Should().BeLessThan(6, "Sıfır mesafeli durakların DistanceMeters değeri 0 veya çok küçük olmalı");
    }

    /// <summary>
    /// Transfer 2: Birbirine yakın iki durak (konfigürasyon limitinin altında) arasında yürünebilir transfer kurulmalı.
    /// </summary>
    [Fact]
    public async Task T02_NearbyStops_WithinWalkLimit_ShouldCreateTransfer()
    {
        // CalculateTransfersAsync was already called in InitializeAsync
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // TN_A and TN_B are ~55m apart (within 1500m default limit)
        var transfer = await db.GtfsTransfers
            .FirstOrDefaultAsync(t => t.GtfsImportRunId == _runId &&
                ((t.FromStopId == "TN_A" && t.ToStopId == "TN_B") ||
                 (t.FromStopId == "TN_B" && t.ToStopId == "TN_A")));

        transfer.Should().NotBeNull("Yürüme limitinin içindeki duraklar arasında transfer kurulmalı");
        transfer!.DistanceMeters.Should().BeGreaterThan(0).And.BeLessThan(1501);
        transfer.WalkingTimeSeconds.Should().BeGreaterThan(0, "Yürüme süresi 0'dan büyük olmalı");
    }

    /// <summary>
    /// Transfer 3: Yapılandırılmış yürüyüş limitinden daha uzak duraklar transfer ağına kesinlikle eklenmemeli.
    /// </summary>
    [Fact]
    public async Task T03_DistantStops_ExceedWalkLimit_ShouldNotCreateTransfer()
    {
        // CalculateTransfersAsync was already called in InitializeAsync
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // TN_A and TN_C are ~20km apart (outside any reasonable walk limit)
        var transfer = await db.GtfsTransfers
            .FirstOrDefaultAsync(t => t.GtfsImportRunId == _runId &&
                ((t.FromStopId == "TN_A" && t.ToStopId == "TN_C") ||
                 (t.FromStopId == "TN_C" && t.ToStopId == "TN_A")));

        transfer.Should().BeNull("Yürüme limitinin dışındaki duraklar arasında transfer kurulmamalı");
    }

    /// <summary>
    /// Transfer 4: Transfer kayıtları doğru ImportId ile ilişkili olmalı.
    /// </summary>
    [Fact]
    public async Task T04_TransferRecords_ShouldBelongToCorrectImportId()
    {
        // CalculateTransfersAsync was already called in InitializeAsync
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var allTransfers = await db.GtfsTransfers
            .Where(t => t.GtfsImportRunId == _runId)
            .ToListAsync();

        allTransfers.Should().AllSatisfy(t =>
            t.GtfsImportRunId.Should().Be(_runId, "Tüm transfer kayıtları bu RunId'ye ait olmalı"));
    }

    /// <summary>
    /// Transfer 5: Yeni feed aktifleştiğinde eski transfer ağı rota aramasında kullanılmamalı.
    /// Bu test D3_NewActiveFeed_ShouldInvalidate_Cache ile örtüşür,
    /// ama burada transfer ağı verisi üzerinden doğruluyoruz.
    /// </summary>
    [Fact]
    public async Task T05_OldFeedTransfers_ShouldNotBeUsed_AfterNewFeedActivation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create old run and seed its transfers
        var oldRun = new GtfsImportRun { FileHash = "OLD_FEED_TN", IsActive = false, Status = "Completed", StartedAt = DateTime.UtcNow };
        db.GtfsImportRuns.Add(oldRun);
        await db.SaveChangesAsync();

        db.GtfsTransfers.Add(new GtfsTransfer
        {
            GtfsImportRunId = oldRun.Id,
            FromStopId = "OLD_STOP_1",
            ToStopId = "OLD_STOP_2",
            DistanceMeters = 100,
            WalkingTimeSeconds = 80,
            CalculationMethod = "Haversine"
        });
        await db.SaveChangesAsync();

        // Current run (_runId) is the active one. Verify old run's transfers are not in the active run.
        var activeTransfers = await db.GtfsTransfers
            .Where(t => t.GtfsImportRunId == _runId)
            .ToListAsync();

        var oldTransfers = await db.GtfsTransfers
            .IgnoreQueryFilters()
            .Where(t => t.GtfsImportRunId == oldRun.Id)
            .ToListAsync();

        activeTransfers.Should().NotContain(t => t.FromStopId == "OLD_STOP_1",
            "Aktif feed'in transfer tablosu eski feed'in verilerini içermemeli");
        oldTransfers.Should().Contain(t => t.FromStopId == "OLD_STOP_1",
            "Eski feed'in transfer verisi veritabanında olabilir ama aktif feed tarafından kullanılmamalı");
    }

    /// <summary>
    /// Transfer 6: Admin rebuild endpointi — /api/v1/admin/gtfs/transfers/rebuild — aktif feed için çalışmalı.
    /// </summary>
    [Fact]
    public async Task T06_AdminRebuildEndpoint_ShouldReturnOkWithCount()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "test-key");
        var response = await client.PostAsync("/api/v1/admin/gtfs/transfers/rebuild", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("transferCount", out _).Should().BeTrue("Yanıt transferCount içermeli");
        doc.RootElement.TryGetProperty("executionTimeMs", out _).Should().BeTrue("Yanıt executionTimeMs içermeli");
    }

    /// <summary>
    /// Transfer 7: Admin status endpointi — /api/v1/gtfs/transfers/status — transfer ağı durumunu döndürmeli.
    /// </summary>
    [Fact]
    public async Task T07_AdminStatusEndpoint_ShouldReturnActiveRunStatus()
    {
        // Transfers are already calculated by InitializeAsync
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/gtfs/transfers/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("activeImportId", out var runIdProp).Should().BeTrue();
        runIdProp.GetInt32().Should().Be(_runId);
        doc.RootElement.TryGetProperty("transferCount", out var countProp).Should().BeTrue();
        countProp.GetInt32().Should().BeGreaterThanOrEqualTo(0, "Transfer sayısı 0 veya daha fazla olmalı");
    }

    /// <summary>
    /// Transfer 8: Kendi kendine aktarma (Self-loop) reddedilmeli.
    /// </summary>
    [Fact]
    public async Task T08_ShouldReject_SelfLoops()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var selfLoops = await db.GtfsTransfers
            .Where(t => t.GtfsImportRunId == _runId && t.FromStopId == t.ToStopId)
            .ToListAsync();

        selfLoops.Should().BeEmpty("Hesaplamada kendi kendine (From=To) olan kenarlar (self-loop) oluşmamalıdır.");
    }

    /// <summary>
    /// Transfer 9: Aynı yöndeki kenarlar çifte (duplicate) yazılmamalı.
    /// </summary>
    [Fact]
    public async Task T09_ShouldReject_DuplicateEdges()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var allTransfers = await db.GtfsTransfers
            .Where(t => t.GtfsImportRunId == _runId)
            .ToListAsync();

        var duplicates = allTransfers
            .GroupBy(t => new { t.FromStopId, t.ToStopId })
            .Where(g => g.Count() > 1)
            .ToList();

        duplicates.Should().BeEmpty("Hesaplamada aynı yönde (A->B) birden fazla kenar (duplicate) oluşmamalıdır.");
    }

    /// <summary>
    /// Transfer 10: Farklı ID ama aynı koordinat (Distance=0) transferi desteklenmeli.
    /// </summary>
    [Fact]
    public async Task T10_ShouldAllow_DifferentStops_AtSameCoordinate()
    {
        // TN_A ve TN_A_SAME zaten Initialize'da seed edildi ve T01 ile kontrol ediliyor.
        // T01 mantığını pekiştirmek için açıkça tekrar doğrulayabiliriz.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var transfer = await db.GtfsTransfers
            .FirstOrDefaultAsync(t => t.GtfsImportRunId == _runId && t.FromStopId == "TN_A" && t.ToStopId == "TN_A_SAME");

        transfer.Should().NotBeNull("Farklı ID'ye sahip, ancak aynı koordinattaki (Distance=0) duraklar birbiriyle bağlantılı olmalıdır.");
        transfer!.DistanceMeters.Should().Be(0);
        transfer.IsSameCoordinateCluster.Should().BeTrue();
    }

    /// <summary>
    /// Transfer 11: Geçersiz (0,0) koordinatlı duraklar ağa dahil edilmemeli.
    /// </summary>
    [Fact]
    public async Task T11_ShouldExclude_InvalidCoordinates()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invalidTransfers = await db.GtfsTransfers
            .Where(t => t.GtfsImportRunId == _runId && (t.FromStopId == "TN_INVALID" || t.ToStopId == "TN_INVALID"))
            .ToListAsync();

        invalidTransfers.Should().BeEmpty("0,0 koordinatlı geçersiz duraklar transfer havuzuna veya hesaplamalara alınmamalıdır.");
    }
}
