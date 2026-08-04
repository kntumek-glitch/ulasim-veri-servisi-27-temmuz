using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using TransportDataService.Models;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Models;
using ulasim_veri_servisi.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;

namespace TransportDataService.Tests.UnitTests.Routing;

public class OsrmWalkingRouteProviderTests
{
    private readonly OsrmConfiguration _config;

    public OsrmWalkingRouteProviderTests()
    {
        _config = new OsrmConfiguration
        {
            BaseUrl = "http://fake-osrm",
            Profile = "foot",
            TimeoutSeconds = 5
        };
    }

    private OsrmWalkingRouteProvider CreateProvider(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri(_config.BaseUrl) };

        var optionsMock = new Mock<IOptions<OsrmConfiguration>>();
        optionsMock.Setup(o => o.Value).Returns(_config);

        return new OsrmWalkingRouteProvider(client, optionsMock.Object, NullLogger<OsrmWalkingRouteProvider>.Instance);
    }

    [Fact]
    public async Task GetWalkingRouteAsync_Success_ReturnsValidDistanceAndDuration()
    {
        // Arrange
        var fakeResponse = new
        {
            code = "Ok",
            routes = new[]
            {
                new { distance = 250.5, duration = 120.3, geometry = "fake_geometry" }
            },
            waypoints = new[]
            {
                new { distance = 5.0, location = new[] { 28.9, 41.0 } },
                new { distance = 10.0, location = new[] { 29.0, 41.1 } }
            }
        };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(fakeResponse))
            });

        var provider = CreateProvider(handler.Object);

        // Act
        var result = await provider.GetWalkingRouteAsync(41.0, 28.9, 41.1, 29.0, true, CancellationToken.None);

        // Assert
        result.State.IsSuccess.Should().BeTrue();
        result.DistanceMeters.Should().Be(250.5);
        result.DurationSeconds.Should().Be(120.3);
        result.GeometryGeoJson?.ToString().Should().Be("fake_geometry");
    }

    [Fact]
    public async Task GetWalkingRouteAsync_NoRoute_ReturnsFailureState()
    {
        // Arrange
        var fakeResponse = new { code = "NoRoute", message = "Cannot find route" };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(fakeResponse))
            });

        var provider = CreateProvider(handler.Object);

        // Act
        var result = await provider.GetWalkingRouteAsync(41.0, 28.9, 41.1, 29.0, false, CancellationToken.None);

        // Assert
        result.State.IsSuccess.Should().BeFalse();
        result.State.ErrorCode.Should().Be("NO_ROUTE");
    }

    [Fact]
    public async Task GetWalkingRouteAsync_DistantSnap_ReturnsUnroutableLocation()
    {
        // Arrange
        var fakeResponse = new
        {
            code = "Ok",
            routes = new[] { new { distance = 10.0, duration = 10.0, geometry = "" } },
            waypoints = new[]
            {
                new { distance = 105.0, location = new[] { 28.9, 41.0 } }, // Over 100m!
                new { distance = 10.0, location = new[] { 29.0, 41.1 } }
            }
        };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(fakeResponse))
            });

        var provider = CreateProvider(handler.Object);

        // Act
        var result = await provider.GetWalkingRouteAsync(41.0, 28.9, 41.1, 29.0, false, CancellationToken.None);

        // Assert
        result.State.IsSuccess.Should().BeFalse();
        result.State.ErrorCode.Should().Be("UNROUTABLE_LOCATION");
    }

    [Fact]
    public async Task GetWalkingRouteAsync_Http500_ReturnsFailureState()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        var provider = CreateProvider(handler.Object);

        // Act
        var result = await provider.GetWalkingRouteAsync(41.0, 28.9, 41.1, 29.0, false, CancellationToken.None);

        // Assert
        result.State.IsSuccess.Should().BeFalse();
        result.State.ErrorCode.Should().Be("API_ERROR");
    }

    [Fact]
    public async Task GetWalkingRouteAsync_Timeout_ReturnsFailureState()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new TaskCanceledException());

        var provider = CreateProvider(handler.Object);

        // Act
        var result = await provider.GetWalkingRouteAsync(41.0, 28.9, 41.1, 29.0, false, CancellationToken.None);

        // Assert
        result.State.IsSuccess.Should().BeFalse();
        result.State.ErrorCode.Should().Be("TIMEOUT");
    }
}
