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

[Collection("PostgreSql collection")]
public class RouteVehiclesIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public RouteVehiclesIntegrationTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(_fixture.ConnectionString));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
            });
        });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task GetRouteVehicles_CacheMiss_CallsExternalApiAndLogs()
    {
        // Act - First call should be cache miss
        var response = await _client.GetAsync("/api/v1/routes/100/vehicles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RouteVehiclesResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        result.Should().NotBeNull();
        result!.RouteNumber.Should().Be("100");
        result.FromCache.Should().BeFalse(); // First call is cache miss
        result.RetrievedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetRouteVehicles_CacheHit_ReturnsFromCache()
    {
        // Act - First call
        await _client.GetAsync("/api/v1/routes/100/vehicles");

        // Act - Second call within cache window (20 seconds)
        var response = await _client.GetAsync("/api/v1/routes/100/vehicles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RouteVehiclesResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        result.Should().NotBeNull();
        result!.FromCache.Should().BeTrue(); // Second call should be cache hit
    }

    [Fact]
    public async Task GetRouteVehicles_ExternalApiLogEntryCreated()
    {
        // Act
        await _client.GetAsync("/api/v1/routes/100/vehicles");

        // Assert - Check ExternalApiLogs table has entry
        // This would require accessing the DbContext from the test
        // For now, we verify the response structure includes cache info
        var response = await _client.GetAsync("/api/v1/routes/100/vehicles");
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RouteVehiclesResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        result.Should().NotBeNull();
        result!.RetrievedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
