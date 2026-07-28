using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net;
using Testcontainers.PostgreSql;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Tests.Helpers;
using ulasım_veri_servisi.Models.Gtfs;
using ulasım_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

public class GtfsImportLifecycleTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("transport_test_db").WithUsername("postgres").WithPassword("postgres123").Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _factory = new WebApplicationFactory<Program>();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync().AsTask();

    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _zipData;
        public MockHttpMessageHandler(byte[] zipData) => _zipData = zipData;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_zipData)
            });
        }
    }

    private HttpClient CreateClient(byte[] zipData)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>("AdminSettings:ApiKey", "test-key")
                }!);
            });

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_db.GetConnectionString()));

                // Register mock HTTP handler for the import service
                services.AddHttpClient<IGtfsImportService, GtfsImportService>()
                        .ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler(zipData));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
            });
        }).CreateClient();
    }

    [Fact]
    public async Task SuccessfulImport_StatusCompleted_And_FinishedAtNotNull()
    {
        var client = CreateClient(MinimalGtfsZipBuilder.Build());
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var importRunDto = System.Text.Json.JsonSerializer.Deserialize<GtfsImportResponseDto>(
            content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        importRunDto.Should().NotBeNull();
        importRunDto!.Status.Should().Be("Completed");
        importRunDto.FinishedAt.Should().NotBeNull();

        // Verify Real DB State
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var importRun = await db.GtfsImportRuns.OrderByDescending(r => r.Id).FirstAsync();
        
        importRun.Status.Should().Be("Completed");
        importRun.FinishedAt.Should().NotBeNull();
        importRun.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SameHash_RejectsImport_And_ReturnsSkipped()
    {
        var zipData = MinimalGtfsZipBuilder.Build();
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var fileHash = Convert.ToHexString(sha256.ComputeHash(zipData)).ToLowerInvariant();

        // Setup initial state in DB
        var client = CreateClient(zipData);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GtfsImportRuns.Add(new GtfsImportRun { FileHash = fileHash, Status = "Completed", FinishedAt = DateTime.UtcNow, IsActive = true });
            db.GtfsStops.Add(new GtfsStop { StopId = "1" }); // Must have stops to skip
            db.SaveChanges();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var importRunDto = System.Text.Json.JsonSerializer.Deserialize<GtfsImportResponseDto>(
            content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        importRunDto.Should().NotBeNull();
        importRunDto!.Status.Should().Be("Skipped");
    }

    [Fact]
    public async Task Cleanup_AbandonedImports_SetsThemToFailed()
    {
        var zipData = MinimalGtfsZipBuilder.Build();
        var client = CreateClient(zipData);
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GtfsImportRuns.Add(new GtfsImportRun { Status = "Running", StartedAt = DateTime.UtcNow.AddHours(-1) });
            db.SaveChanges();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");
        await client.SendAsync(request);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldRun = await db.GtfsImportRuns.OrderBy(r => r.Id).FirstAsync();
            oldRun.Status.Should().Be("Failed");
            oldRun.ErrorMessage.Should().Contain("Abandoned");
        }
    }
}
