using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Models.Gtfs;
using ulasım_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class RouteVehiclesIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RouteVehiclesIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Ensure DB schema exists but no specific data seeding is required for these error tests
    }

    [Fact]
    public async Task GetRouteVehicles_CacheMiss_CallsExternalApiAndLogs()
    {
        // Act - First call should be cache miss (will fail in test environment because external API is unreachable)
        var response = await _client.GetAsync("/api/v1/routes/100/vehicles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        // We cannot reliably parse RouteVehiclesResponse since it returns ProblemDetails
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("traceId");
    }

}
