using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.Transfers;
using ulasim_veri_servisi.Services.Interfaces;
using Moq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class GtfsTransfersControllerTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private int _runId;

    public GtfsTransfersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRun = await db.GtfsImportRuns.FirstOrDefaultAsync(r => r.IsActive);
        if (activeRun == null)
        {
            activeRun = new GtfsImportRun
            {
                FileHash = Guid.NewGuid().ToString(),
                IsActive = true,
                Status = "Completed",
                StartedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow.AddMinutes(5),
                FeedVersion = "vTest"
            };
            db.GtfsImportRuns.Add(activeRun);
            await db.SaveChangesAsync();
        }
        _runId = activeRun.Id;

        // Seed some dummy transfers if not exist
        if (!await db.GtfsTransfers.AnyAsync(t => t.GtfsImportRunId == _runId))
        {
            db.GtfsTransfers.AddRange(
                new GtfsTransfer { GtfsImportRunId = _runId, FromStopId = "S1", ToStopId = "S2", DistanceMeters = 500, WalkingTimeSeconds = 400, CalculationMethod = "Haversine" },
                new GtfsTransfer { GtfsImportRunId = _runId, FromStopId = "S2", ToStopId = "S3", DistanceMeters = 600, WalkingTimeSeconds = 500, CalculationMethod = "Haversine" }
            );
            
            db.GtfsImportPhases.Add(new GtfsImportPhase
            {
                GtfsImportRunId = _runId,
                PhaseName = "CalculatingTransfers",
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                FinishedAt = DateTime.UtcNow,
                ProcessedRecordCount = 2
            });

            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rebuild_WithoutAdminKey_ShouldReturn401()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/gtfs/transfers/rebuild");
        
        var response = await client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rebuild_WithInvalidAdminKey_ShouldReturn403()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/gtfs/transfers/rebuild");
        request.Headers.Add("X-Admin-Key", "invalid-key");
        
        var response = await client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rebuild_WithValidAdminKey_ShouldReturn200()
    {
        var mockService = new Mock<IGtfsTransferCalculationService>();
        mockService.Setup(s => s.RebuildTransfersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(150);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(sp => mockService.Object);
            });
        }).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/gtfs/transfers/rebuild");
        request.Headers.Add("X-Admin-Key", "test-key");
        
        var response = await client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rebuild_Concurrency_SecondRequestShouldReturn409()
    {
        // To test concurrency, we need the calculation service to take some time.
        // We will create a client with a mock calculation service that delays.
        
        var mockService = new Mock<IGtfsTransferCalculationService>();
        mockService.Setup(s => s.RebuildTransfersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(2000); // Wait 2 seconds to simulate long work
                return 100;
            });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(sp => mockService.Object);
            });
        }).CreateClient();

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/gtfs/transfers/rebuild");
        request1.Headers.Add("X-Admin-Key", "test-key");
        
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/gtfs/transfers/rebuild");
        request2.Headers.Add("X-Admin-Key", "test-key");

        // Act
        var task1 = client.SendAsync(request1);
        await Task.Delay(100); // ensure task1 starts
        var task2 = client.SendAsync(request2);

        var responses = await Task.WhenAll(task1, task2);

        // Assert
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rebuild_WithException_ShouldRollbackAndKeepOldData()
    {
        // Setup mock to throw exception
        var mockService = new Mock<IGtfsTransferCalculationService>();
        mockService.Setup(s => s.RebuildTransfersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Mock calculation error"));

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(sp => mockService.Object);
            });
        }).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/gtfs/transfers/rebuild");
        request.Headers.Add("X-Admin-Key", "test-key");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Verify old data is still there
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.GtfsTransfers.CountAsync(t => t.GtfsImportRunId == _runId);
        
        count.Should().Be(2, "eski veriler korunmali");
    }

    [Fact]
    public async Task Status_ReturnsCorrectResponseSchemaAndData()
    {
        var client = _factory.CreateClient();
        
        var response = await client.GetAsync("/api/v1/gtfs/transfers/status");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<TransferNetworkStatusResponse>();
        result.Should().NotBeNull();
        result!.ActiveImportId.Should().Be(_runId);
        result.TransferCount.Should().Be(2);
        result.IsReady.Should().BeTrue();
        result.CalculationMethod.Should().Be("Haversine");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activeRun = await db.GtfsImportRuns.FindAsync(_runId);
        
        result.DataVersion.Should().Be(activeRun!.FeedVersion ?? activeRun.FileHash ?? activeRun.Id.ToString());
        result.ProcessingTimeMs.Should().NotBeNull();
    }
}
