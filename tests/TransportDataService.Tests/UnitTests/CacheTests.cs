using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using TransportDataService;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

[Collection("PostgreSql collection")]
public class CacheTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _context = null!;
    private Mock<ILogger<ExternalEshotService>> _loggerMock;

    public CacheTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();
        await TruncateExternalApiLogsAsync();
        _loggerMock = new Mock<ILogger<ExternalEshotService>>();
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        return Task.CompletedTask;
    }

    private async Task TruncateExternalApiLogsAsync()
    {
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"ExternalApiLogs\" RESTART IDENTITY CASCADE");
    }

    [Fact]
    public async Task ExternalEshotService_CacheMiss_CallsHttpClient()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://openapi.izmir.bel.tr")
        };

        var responseContent = "[]"; // EshotBusDto list
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent)
        };

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        var service = new ExternalEshotService(httpClient, _context, cache, _loggerMock.Object);

        // Act
        var result = await service.GetApproachingBusesAsync("123");

        // Assert
        result.Should().NotBeNull();
        result.FromCache.Should().BeFalse();
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task ExternalEshotService_CacheHit_DoesNotCallHttpClient()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://openapi.izmir.bel.tr")
        };

        var service = new ExternalEshotService(httpClient, _context, cache, _loggerMock.Object);

        // Seed cache
        var cachedBuses = new List<EshotBusDto> { new EshotBusDto { HatNumarasi = 123 } };
        cache.Set("approaching-buses:123", cachedBuses);

        // Act
        var result = await service.GetApproachingBusesAsync("123");

        // Assert
        result.Should().NotBeNull();
        result.FromCache.Should().BeTrue();
        result.Data.Should().BeEquivalentTo(cachedBuses);

        // HttpClient should NOT be called
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task ExternalEshotService_RouteVehicles_CacheMiss_CallsHttpClient()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://openapi.izmir.bel.tr")
        };

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"HataVarMi\":false,\"HatOtobusKonumlari\":[]}")
            });

        var service = new ExternalEshotService(httpClient, _context, cache, _loggerMock.Object);
        var result = await service.GetRouteVehiclesAsync("123");

        result.FromCache.Should().BeFalse();
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExternalEshotService_RouteVehicles_CacheHit_SkipsHttpAndLogging()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://openapi.izmir.bel.tr")
        };

        cache.Set("route-vehicles:456", new List<RouteVehicleDto> { new() { OtobusId = 1 } });
        var service = new ExternalEshotService(httpClient, _context, cache, _loggerMock.Object);

        var result = await service.GetRouteVehiclesAsync("456");

        result.FromCache.Should().BeTrue();
        (await _context.ExternalApiLogs.CountAsync()).Should().Be(0);
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}

