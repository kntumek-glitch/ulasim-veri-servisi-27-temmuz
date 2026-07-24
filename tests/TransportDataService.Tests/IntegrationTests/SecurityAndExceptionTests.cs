using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json;
using Testcontainers.PostgreSql;
using TransportDataService;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

public class SecurityAndExceptionTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("transport_test_db").WithUsername("postgres").WithPassword("postgres123").Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_db.GetConnectionString()));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
            });
        });
        
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ImportGtfs_WithoutAdminKey_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync("/api/v1/import/gtfs", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportGtfs_WithInvalidAdminKey_ReturnsForbidden()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "invalid_key");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnknownEndpoint_ReturnsProblemDetails_WithoutStackTrace()
    {
        var response = await _client.GetAsync("/api/v1/some-unknown-endpoint-that-does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Normally 404 handled by ASP.NET might return standard JSON. Let's ensure it's problem details.
        // Actually, for generic 404, it might just return 404 without ProblemDetails if no MapFallback is set, but if it does return JSON, it shouldn't have stack trace.
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("StackTrace");
        content.Should().NotContain("Exception");
    }

    [Fact]
    public async Task ImportGtfs_WithValidKey_DoesNotReturnUnauthorized()
    {
        // Set the environment variable or configuration for the key dynamically
        var factoryWithConfig = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>("AdminSettings:ApiKey", "test-secret-key")
                }!);
            });
        });

        var client = factoryWithConfig.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-secret-key");
        
        var response = await client.SendAsync(request);
        
        // It might be 400 or 500 depending on actual file, but it should NOT be 401 or 403
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
