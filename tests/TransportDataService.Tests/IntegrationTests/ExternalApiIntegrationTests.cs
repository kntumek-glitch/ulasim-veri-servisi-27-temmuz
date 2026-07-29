using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Exceptions;
using ulasım_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class ExternalApiIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ExternalApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithEshot(Func<IServiceProvider, IExternalEshotService> factoryMethod)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(IExternalEshotService)).ToList())
                    services.Remove(d);

                services.AddScoped(factoryMethod);
            });
        }).CreateClient();
    }

    private static async Task<ProblemDetails> ReadProblem(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ProblemDetails>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public async Task GetRouteVehicles_EshotBadGateway_Returns502ProblemDetails()
    {
        var client = CreateClientWithEshot(sp =>
            new ThrowingEshotService(new BadGatewayException("ESHOT servisinden veri alınamadı.")));

        var response = await client.GetAsync("/api/v1/routes/123/vehicles");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var problem = await ReadProblem(response);
        problem.Status.Should().Be(502);
        problem.Title.Should().Be("Dış servis hatası");
    }

    [Fact]
    public async Task GetRouteVehicles_EshotUnavailable_Returns503ProblemDetails()
    {
        var client = CreateClientWithEshot(sp =>
            new ThrowingEshotService(new ServiceUnavailableException("ESHOT servisine ulaşılamıyor.")));

        var response = await client.GetAsync("/api/v1/routes/123/vehicles");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await ReadProblem(response);
        problem.Status.Should().Be(503);
        problem.Title.Should().Be("Servis kullanılamıyor");
    }

    [Fact]
    public async Task GetRouteVehicles_SecondCall_ReturnsFromCache()
    {
        var callCount = 0;
        var client = CreateClientWithEshot(sp =>
        {
            var context = sp.GetRequiredService<AppDbContext>();
            var cache = sp.GetRequiredService<IMemoryCache>();
            var logger = sp.GetRequiredService<ILogger<ExternalEshotService>>();
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent("{\"HataVarMi\":false,\"HatOtobusKonumlari\":[]}")
                    };
                });
            var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://openapi.izmir.bel.tr") };
            return new ExternalEshotService(httpClient, context, cache, logger);
        });

        var firstResponse = await client.GetAsync("/api/v1/routes/99/vehicles");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        if (!firstResponse.IsSuccessStatusCode) throw new Exception(firstBody);
        var first = JsonDocument.Parse(firstBody);

        var secondResponse = await client.GetAsync("/api/v1/routes/99/vehicles");
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        if (!secondResponse.IsSuccessStatusCode) throw new Exception(secondBody);
        var second = JsonDocument.Parse(secondBody);

        first.RootElement.GetProperty("fromCache").GetBoolean().Should().BeFalse();
        second.RootElement.GetProperty("fromCache").GetBoolean().Should().BeTrue();
        callCount.Should().Be(1);
    }

    private sealed class ThrowingEshotService : IExternalEshotService
    {
        private readonly Exception _ex;
        public ThrowingEshotService(Exception ex) => _ex = ex;

        public Task<CachedResult<List<EshotBusDto>>> GetApproachingBusesAsync(string externalStopId, CancellationToken cancellationToken = default)
            => Task.FromException<CachedResult<List<EshotBusDto>>>(_ex);

        public Task<CachedResult<List<RouteVehicleDto>>> GetRouteVehiclesAsync(string routeNumber, CancellationToken cancellationToken = default)
            => Task.FromException<CachedResult<List<RouteVehicleDto>>>(_ex);
    }
}
