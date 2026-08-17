using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TransportDataService.Domain;
using TransportDataService.Models;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Models;
using ulasim_veri_servisi.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class JourneyPlanningRoutingIntegrationTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Mock<IWalkingRouteProvider> _mockRoutingProvider;
    private int _runId;

    public JourneyPlanningRoutingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _mockRoutingProvider = new Mock<IWalkingRouteProvider>();
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

        var transferService = scope.ServiceProvider.GetRequiredService<ulasim_veri_servisi.Services.Interfaces.IGtfsTransferCalculationService>();
        await transferService.CalculateTransfersAsync(_runId, CancellationToken.None);

        var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        if (cache is Microsoft.Extensions.Caching.Memory.MemoryCache memoryCache)
        {
            memoryCache.Clear();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task SeedDataAsync(AppDbContext db, int runId)
    {
        db.GtfsAgencies.Add(new GtfsAgency { AgencyId = "AG1", AgencyName = "Test", AgencyTimezone = "Europe/Istanbul", GtfsImportRunId = runId });
        
        // Origin -> Walk -> S1
        // T1 on R1 leaves S1 at 08:00
        // T2 on R1 leaves S1 at 08:15 (next trip)
        // Dest -> Walk -> S2 (Arrival at 08:30 for T1, 08:45 for T2)

        var s1 = new GtfsStop { StopId = "S1", StopName = "OriginStop", StopLat = 38.4, StopLon = 27.1, GtfsImportRunId = runId };
        var s2 = new GtfsStop { StopId = "S2", StopName = "DestStop", StopLat = 38.41, StopLon = 27.11, GtfsImportRunId = runId };
        db.GtfsStops.AddRange(s1, s2);

        var r1 = new GtfsRoute { RouteId = "R1", RouteShortName = "100", GtfsImportRunId = runId };
        db.GtfsRoutes.Add(r1);

        db.GtfsCalendars.Add(new GtfsCalendar
        {
            ServiceId = "SRV_EVERYDAY",
            Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true,
            StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31), GtfsImportRunId = runId
        });

        var t1 = new GtfsTrip { Route = r1, TripId = "T1", RouteId = "R1", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Dest", DirectionId = 0, ShapeId = "SHAPE_1", GtfsImportRunId = runId };
        var t2 = new GtfsTrip { Route = r1, TripId = "T2", RouteId = "R1", ServiceId = "SRV_EVERYDAY", TripHeadsign = "Dest", DirectionId = 0, ShapeId = "SHAPE_1", GtfsImportRunId = runId };
        db.GtfsTrips.AddRange(t1, t2);

        db.GtfsStopTimes.AddRange(
            new GtfsStopTime { Trip = t1, Stop = s1, TripId = "T1", StopId = "S1", StopSequence = 1, ArrivalSeconds = 8*3600, DepartureSeconds = 8*3600, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t1, Stop = s2, TripId = "T1", StopId = "S2", StopSequence = 2, ArrivalSeconds = 8*3600 + 1800, DepartureSeconds = 8*3600 + 1800, GtfsImportRunId = runId },
            new GtfsStopTime { Trip = t2, Stop = s1, TripId = "T2", StopId = "S1", StopSequence = 1, ArrivalSeconds = 8*3600 + 900, DepartureSeconds = 8*3600 + 900, GtfsImportRunId = runId }, // 08:15
            new GtfsStopTime { Trip = t2, Stop = s2, TripId = "T2", StopId = "S2", StopSequence = 2, ArrivalSeconds = 8*3600 + 2700, DepartureSeconds = 8*3600 + 2700, GtfsImportRunId = runId } // 08:45
        );
        return Task.CompletedTask;
    }

    private JourneyPlanningService CreateServiceWithMockRouting()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IWalkingRouteProvider>(_ => _mockRoutingProvider.Object);
            });
        }).CreateClient();
        
        var scope = _factory.Services.CreateScope();
        // Create an instance of JourneyPlanningService injecting the mocked provider manually
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JourneyPlanningService>>();
        
        var options = Microsoft.Extensions.Options.Options.Create(new WalkingRoutingCacheConfiguration { TtlMinutes = 1, MaxCapacity = 10 });
        var walkingRoutingService = new WalkingRoutingService(_mockRoutingProvider.Object, cache, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<WalkingRoutingService>.Instance);
        
        var spatialService = new ulasim_veri_servisi.Services.JourneyPlanning.Spatial.SpatialCalculatorService();
        var cacheService = new ulasim_veri_servisi.Services.JourneyPlanning.DataAccess.JourneyCacheService(db, cache, spatialService);
        var routingEngine = new ulasim_veri_servisi.Services.JourneyPlanning.Algorithms.JourneyRoutingEngine(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<ulasim_veri_servisi.Services.JourneyPlanning.Algorithms.JourneyRoutingEngine>.Instance);
        var mapper = new ulasim_veri_servisi.Services.JourneyPlanning.Mapping.JourneyResultMapper(db, walkingRoutingService, config);
        
        return new JourneyPlanningService(db, config, cache, logger, new ulasim_veri_servisi.Services.JourneyPlanCacheTokenSource(), walkingRoutingService, cacheService, spatialService, routingEngine, mapper);
    }

    [Fact]
    public async Task SearchJourney_ExactWalkTimeMissesConnection_FallsBackToNextTrip()
    {
        // Arrange
        // Departure requested at 07:55
        // Haversine might say it takes 2 minutes to walk to S1 -> we arrive at 07:57 -> we catch T1 at 08:00
        // BUT OSRM returns 8 minutes! -> we arrive at S1 at 08:03 -> MISS T1!
        // Should fallback to T2 which departs at 08:15
        
        var service = CreateServiceWithMockRouting();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 55, 0, TimeSpan.FromHours(3)), // Requesting at 07:55
            MaxWalkingMeters = 3000,
            MaxTransfers = 0
        };

        // Mock OSRM to return 480 seconds (8 mins) for the first walk
        _mockRoutingProvider.Setup(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalkingResult { State = new ErrorState { IsSuccess = true }, DurationSeconds = 480, DistanceMeters = 800 });

        // Act
        var result = await service.SearchJourneyAsync(request, CancellationToken.None);

        // Assert
        result.ReasonCode.Should().Be("SUCCESS");
        result.Itineraries.Should().NotBeEmpty();

        var bestItinerary = result.Itineraries.First();
        var transitLeg = bestItinerary.Legs.First(l => l.Mode == "TRANSIT");
        
        transitLeg.TripId.Should().Be("T2", "Because T1 was missed due to the 8-minute exact walk");
        bestItinerary.IsApproximate.Should().BeFalse();
    }

    [Fact]
    public async Task SearchJourney_UnroutableWalk_DropsCandidate()
    {
        // Arrange
        var service = CreateServiceWithMockRouting();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.395, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.411, Lon = 27.111 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3))
        };

        // Mock OSRM to return UNROUTABLE_LOCATION for the first walk
        _mockRoutingProvider.Setup(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalkingResult { State = new ErrorState { IsSuccess = false, ErrorCode = "UNROUTABLE_LOCATION" } });

        // Act
        var result = await service.SearchJourneyAsync(request, CancellationToken.None);

        // Assert
        result.ReasonCode.Should().Be("NO_ROUTE_FOUND", "Candidate should be dropped because the walk is unroutable");
        result.Itineraries.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchJourney_ProviderTimeout_FallsBackToHaversineWithWarning()
    {
        // Arrange
        var service = CreateServiceWithMockRouting();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.41, Lon = 27.11 },
            DepartureDateTime = new DateTimeOffset(2024, 1, 1, 7, 50, 0, TimeSpan.FromHours(3)),
            MaxWalkingMeters = 3000
        };

        // Mock OSRM to return a timeout error
        _mockRoutingProvider.Setup(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalkingResult { State = new ErrorState { IsSuccess = false, ErrorCode = "PROVIDER_ERROR" } });

        // Act
        var result = await service.SearchJourneyAsync(request, CancellationToken.None);

        // Assert
        result.ReasonCode.Should().Be("SUCCESS");
        result.Itineraries.Should().NotBeEmpty();

        var bestItinerary = result.Itineraries.First();
        
        // Should fallback to T1 using Haversine
        bestItinerary.IsApproximate.Should().BeTrue("Because the provider failed, the fallback Haversine distance must be used and marked as approximate.");
    }
}
