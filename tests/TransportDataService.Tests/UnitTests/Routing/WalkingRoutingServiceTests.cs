using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TransportDataService.Models;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Models;
using ulasim_veri_servisi.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;
using Xunit;

namespace TransportDataService.Tests.UnitTests.Routing;

public class WalkingRoutingServiceTests
{
    private readonly Mock<IWalkingRouteProvider> _providerMock;
    private readonly WalkingRoutingService _service;
    private readonly MemoryCache _cache;

    public WalkingRoutingServiceTests()
    {
        _providerMock = new Mock<IWalkingRouteProvider>();
        
        var options = Options.Create(new WalkingRoutingCacheConfiguration
        {
            TtlMinutes = 10,
            MaxCapacity = 1000
        });

        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new WalkingRoutingService(_providerMock.Object, _cache, options, NullLogger<WalkingRoutingService>.Instance);
    }

    [Fact]
    public async Task CalculateWalkingRouteAsync_CacheHit_DoesNotCallProviderMultipleTimes()
    {
        // Arrange
        var fakeResult = new WalkingResult
        {
            State = new ErrorState { IsSuccess = true },
            DistanceMeters = 100,
            DurationSeconds = 60
        };

        _providerMock.Setup(p => p.GetWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResult);

        // Act
        var result1 = await _service.CalculateWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, "foot", CancellationToken.None);
        var result2 = await _service.CalculateWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, "foot", CancellationToken.None);

        // Assert
        result1.Should().BeEquivalentTo(result2);
        _providerMock.Verify(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CalculateWalkingRouteAsync_ConcurrentRequests_CoalescesToSingleCall()
    {
        // Arrange
        int callCount = 0;
        _providerMock.Setup(p => p.GetWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(100); // Simulate network latency
                return new WalkingResult { State = new ErrorState { IsSuccess = true } };
            });

        // Act - Spawn 100 concurrent requests
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _service.CalculateWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, "foot", CancellationToken.None))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        callCount.Should().Be(1, "Request coalescing should prevent multiple downstream calls for the same route");
        results.All(r => r.State.IsSuccess).Should().BeTrue();
    }

    [Fact]
    public async Task CalculateWalkingRouteAsync_Failure_DoesNotCache()
    {
        // Arrange
        var fakeResult = new WalkingResult
        {
            State = new ErrorState { IsSuccess = false, ErrorCode = "NO_ROUTE" }
        };

        _providerMock.Setup(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResult);

        // Act
        var result1 = await _service.CalculateWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, "foot", CancellationToken.None);
        var result2 = await _service.CalculateWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, "foot", CancellationToken.None);

        // Assert
        result1.State.IsSuccess.Should().BeFalse();
        result2.State.IsSuccess.Should().BeFalse();
        
        // Since it failed, it shouldn't be cached, so the provider should be called twice
        _providerMock.Verify(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CalculateWalkingRouteAsync_ClientCancellation_Throws_DoesNotCacheResult()
    {
        // Arrange
        _providerMock.Setup(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await _service.Invoking(s => s.CalculateWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, "foot", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        
        // Assert that nothing was cached by attempting again without cancellation
        var fakeResult = new WalkingResult { State = new ErrorState { IsSuccess = true } };
        _providerMock.Setup(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResult);

        var result = await _service.CalculateWalkingRouteAsync(41.0, 29.0, 41.1, 29.1, false, "foot", CancellationToken.None);
        result.State.IsSuccess.Should().BeTrue();

        // The provider should have been called twice (first cancelled, second successful)
        _providerMock.Verify(p => p.GetWalkingRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
