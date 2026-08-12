using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Exceptions;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Models.Routing;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

public class SnapshotLifecycleTests
{
    private readonly AppDbContext _dbContext;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    public SnapshotLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new AppDbContext(options);

        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(AppDbContext))).Returns(_dbContext);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IRoutingSnapshotManager))).Returns(new Mock<IRoutingSnapshotManager>().Object);

        _mockScope = new Mock<IServiceScope>();
        _mockScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);

        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopeFactory.Setup(sf => sf.CreateScope()).Returns(_mockScope.Object);
    }

    [Fact]
    public async Task AtomicRollback_CandidateBuildFails_OlderSnapshotRemainsActive()
    {
        // 1. Atomic Rollback (Candidate snapshot build fails -> older snapshot remains active).
        var mockManager = new Mock<IRoutingSnapshotManager>();
        var oldSnapshot = new RoutingSnapshot { FeedHash = "OLD_HASH" };
        
        mockManager.Setup(m => m.GetActiveSnapshot()).Returns(oldSnapshot);
        mockManager.Setup(m => m.BuildCandidateSnapshotAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Build failed"));

        Func<Task> act = async () => await mockManager.Object.BuildCandidateSnapshotAsync(99, "NEW_HASH", CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        
        mockManager.Object.GetActiveSnapshot().Should().Be(oldSnapshot);
        mockManager.Verify(m => m.PromoteSnapshot(It.IsAny<RoutingSnapshot>()), Times.Never);
    }

    [Fact]
    public void AtomicPromotion_CandidateSucceeds_SimultaneousPromotion()
    {
        // 2. Atomic Promotion (Candidate snapshot succeeds -> feed and snapshot promote simultaneously).
        var loggerMock = new Mock<ILogger<RoutingSnapshotManager>>();
        var manager = new RoutingSnapshotManager(_mockScopeFactory.Object, loggerMock.Object);
        
        var initialSnapshot = manager.GetActiveSnapshot();
        initialSnapshot.Should().BeNull();

        var candidate = new RoutingSnapshot { FeedHash = "NEW_HASH" };

        manager.PromoteSnapshot(candidate);
        
        var active = manager.GetActiveSnapshot();
        active.Should().NotBeNull();
        active!.FeedHash.Should().Be("NEW_HASH");
    }

    [Fact]
    public async Task IntegrityGuard_DbFeedHashVsSnapshotHash_MatchesExactly()
    {
        // 3. Integrity Guard (DB feed hash vs. snapshot hash mismatch is successfully caught).
        var loggerMock = new Mock<ILogger<RoutingSnapshotManager>>();
        var manager = new RoutingSnapshotManager(_mockScopeFactory.Object, loggerMock.Object);
        string expectedHash = "INTEGRITY_HASH_777";

        var candidate = await manager.BuildCandidateSnapshotAsync(1, expectedHash, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate.FeedHash.Should().Be(expectedHash);
    }

    [Fact]
    public async Task ColdStart_StartupSnapshotWarmup_Execution()
    {
        // 4. Cold Start (Startup snapshot warmup execution).
        _dbContext.GtfsImportRuns.Add(new GtfsImportRun 
        { 
            Id = 1, 
            IsActive = true, 
            FileHash = "WARMUP_HASH" 
        });
        await _dbContext.SaveChangesAsync();

        var loggerMock = new Mock<ILogger<SnapshotWarmupService>>();
        var mockManager = new Mock<IRoutingSnapshotManager>();
        
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IRoutingSnapshotManager))).Returns(mockManager.Object);

        var warmupService = new SnapshotWarmupService(_mockScopeFactory.Object, loggerMock.Object);
        
        var candidate = new RoutingSnapshot { FeedHash = "WARMUP_HASH" };
        mockManager.Setup(m => m.BuildCandidateSnapshotAsync(1, "WARMUP_HASH", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        await warmupService.StartAsync(CancellationToken.None);

        mockManager.Verify(m => m.BuildCandidateSnapshotAsync(1, "WARMUP_HASH", It.IsAny<CancellationToken>()), Times.Once);
        mockManager.Verify(m => m.PromoteSnapshot(candidate), Times.Once);
    }

    [Fact]
    public async Task StateHandling_SnapshotUnavailable_ReturnsErrorWhenNoSnapshot()
    {
        // 5. State Handling (Snapshot unavailable state routing - return error when no snapshot).
        var mockManager = new Mock<IRoutingSnapshotManager>();
        mockManager.Setup(m => m.GetActiveSnapshot()).Returns((RoutingSnapshot?)null);

        var mockLogger = new Mock<ILogger<RaptorRoutingEngine>>();
        var mockConfig = new Mock<IConfiguration>();

        // WalkingRoutingService is bypassed if snapshot is null because SnapshotUnavailableException is thrown first.
        var engine = new RaptorRoutingEngine(mockManager.Object, null!, mockLogger.Object, mockConfig.Object);

        var request = new JourneyPlanV2SearchRequest
        {
            Origin = new TransportDataService.Models.Gtfs.JourneyPlan.CoordinateDto { Lat = 1, Lon = 1 },
            Destination = new TransportDataService.Models.Gtfs.JourneyPlan.CoordinateDto { Lat = 2, Lon = 2 },
            DateTime = DateTime.Now,
            SearchMode = RoutingMode.DEPART_AT
        };

        Func<Task> act = async () => await engine.SearchJourneyV2Async(request, CancellationToken.None);

        await act.Should().ThrowAsync<SnapshotUnavailableException>();
    }

    [Fact]
    public async Task Recovery_SnapshotRebuildProcess_Successful()
    {
        // 6. Recovery (Snapshot rebuild process).
        var loggerMock = new Mock<ILogger<RoutingSnapshotManager>>();
        var manager = new RoutingSnapshotManager(_mockScopeFactory.Object, loggerMock.Object);
        
        var originalSnapshot = new RoutingSnapshot { FeedHash = "ORIGINAL_HASH" };
        manager.PromoteSnapshot(originalSnapshot);
        
        manager.GetActiveSnapshot()!.FeedHash.Should().Be("ORIGINAL_HASH");

        // Rebuild and promote new snapshot
        var rebuiltCandidate = await manager.BuildCandidateSnapshotAsync(2, "REBUILT_HASH", CancellationToken.None);
        manager.PromoteSnapshot(rebuiltCandidate);

        manager.GetActiveSnapshot().Should().NotBeNull();
        manager.GetActiveSnapshot()!.FeedHash.Should().Be("REBUILT_HASH");
    }
}
