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
public class ExternalApiLoggerTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _context = null!;
    private Mock<ILogger<ExternalEshotService>> _loggerMock;
    private IMemoryCache _cache;

    public ExternalApiLoggerTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();
        await TruncateExternalApiLogsAsync();
        _loggerMock = new Mock<ILogger<ExternalEshotService>>();
        _cache = new MemoryCache(new MemoryCacheOptions());
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
    public async Task GetApproachingBuses_CacheMiss_LogsSuccessfully()
    {
        // Arrange
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://openapi.izmir.bel.tr")
        };

        var responseContent = "[]";
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

        var service = new ExternalEshotService(httpClient, _context, _cache, _loggerMock.Object);

        // Act
        await service.GetApproachingBusesAsync("123");

        // Assert
        var logs = await _context.ExternalApiLogs.ToListAsync();
        logs.Should().HaveCount(1, "Aynı çağrı için yalnızca bir DB logu olmalıdır.");
        
        var log = logs.First();
        log.EndpointName.Should().Be("ApproachingBuses");
        log.IsSuccessful.Should().BeTrue();
        log.HttpStatusCode.Should().Be(200);
        log.ResponseDurationMs.Should().BeGreaterThanOrEqualTo(0);
        log.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GetApproachingBuses_CacheHit_DoesNotCreateLog()
    {
        // Arrange
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://openapi.izmir.bel.tr")
        };

        var service = new ExternalEshotService(httpClient, _context, _cache, _loggerMock.Object);

        // Seed cache
        var cachedBuses = new List<EshotBusDto> { new EshotBusDto { HatNumarasi = 123 } };
        _cache.Set("approaching-buses:123", cachedBuses);

        // Act
        await service.GetApproachingBusesAsync("123");

        // Assert
        var logs = await _context.ExternalApiLogs.ToListAsync();
        logs.Should().BeEmpty("Cache hit durumunda dış API logu oluşmamalıdır.");
    }

    [Fact]
    public async Task GetRouteVehicles_HataVarMiTrue_LogsAsError()
    {
        // Arrange
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://openapi.izmir.bel.tr")
        };

        var responseContent = "{\"HataVarMi\": true, \"HataMesaj\": \"Test Hatasi\"}";
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

        var service = new ExternalEshotService(httpClient, _context, _cache, _loggerMock.Object);

        // Act
        Func<Task> act = async () => await service.GetRouteVehiclesAsync("123");

        // Assert
        await act.Should().ThrowAsync<ulasim_veri_servisi.Exceptions.BadGatewayException>();

        var logs = await _context.ExternalApiLogs.ToListAsync();
        logs.Should().HaveCount(1, "Aynı çağrı için yalnızca bir DB logu olmalıdır.");
        
        var log = logs.First();
        log.EndpointName.Should().Be("RouteVehicles");
        log.IsSuccessful.Should().BeFalse("HataVarMi = true durumunda log IsSuccessful = false olmalıdır.");
        log.ErrorMessage.Should().Be("Test Hatasi");
    }
}

