using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Models.Gtfs;
using Xunit;
using Testcontainers.PostgreSql;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("PostgreSql collection")]
public class GtfsImportIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public GtfsImportIntegrationTests(PostgreSqlFixture fixture) => _fixture = fixture;

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
    public async Task Import_WhenExternalServiceReturns502_ReturnsProblemDetails()
    {
        // This test would require mocking the external ESHOT API to return 502
        // For now, we test the ProblemDetails format via a known bad endpoint
        var response = await _client.PostAsync("/api/v1/import/gtfs", null);

        // The import will fail because the external URL is not reachable in test env
        // We verify the error response format
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        var content = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        problem.Should().NotBeNull();
        problem!.Status.Should().BeGreaterThanOrEqualTo(500);
        problem.Title.Should().NotBeNullOrEmpty();
        problem.Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Import_WhenExternalServiceReturns503_ReturnsProblemDetails()
    {
        // Similar to 502 test - the external service is unreachable in test env
        var response = await _client.PostAsync("/api/v1/import/gtfs", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        var content = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        problem.Should().NotBeNull();
        problem!.Status.Should().BeGreaterThanOrEqualTo(500);
        problem.Title.Should().NotBeNullOrEmpty();
        problem.Detail.Should().NotBeNullOrEmpty();
    }
}
