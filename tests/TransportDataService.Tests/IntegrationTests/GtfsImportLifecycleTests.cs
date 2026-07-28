using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Tests.Helpers;
using ulasım_veri_servisi.Models.Gtfs;
using ulasım_veri_servisi.Services;
using Xunit;
using Microsoft.AspNetCore.Mvc;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class GtfsImportLifecycleTests
{
    private readonly CustomWebApplicationFactory _factory;

    public GtfsImportLifecycleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[]? _zipData;
        private readonly HttpStatusCode _statusCode;
        
        public MockHttpMessageHandler(byte[]? zipData, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _zipData = zipData;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_zipData != null)
            {
                response.Content = new ByteArrayContent(_zipData);
            }
            return Task.FromResult(response);
        }
    }

    private HttpClient CreateClient(byte[]? zipData, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Register mock HTTP handler for the import service
                services.AddHttpClient<IGtfsImportService, GtfsImportService>()
                        .ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler(zipData, statusCode));
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
        response.StatusCode.Should().Be(HttpStatusCode.Created); // Task 3 fix

        var content = await response.Content.ReadAsStringAsync();
        var importRunDto = JsonSerializer.Deserialize<GtfsImportResponseDto>(
            content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
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
        var fileHash = Convert.ToHexString(sha256.ComputeHash(zipData)); // Task 4 fix (no ToLowerInvariant)

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
        var importRunDto = JsonSerializer.Deserialize<GtfsImportResponseDto>(
            content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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

    [Fact]
    public async Task ImportGtfs_MissingOptionalFiles_ClearsTargetTables()
    {
        // 1. Arrange - Seed DB with some old optional data
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GtfsCalendars.Add(new GtfsCalendar { ServiceId = "OLD_SRV", Monday = true, Tuesday = true, StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2020, 12, 31) });
            db.GtfsShapePoints.Add(new GtfsShapePoint { ShapeId = "OLD_SHP", Latitude = 38.0, Longitude = 27.0, Sequence = 1 });
            db.SaveChanges();
        }

        // 2. Build ZIP missing calendar.txt and shapes.txt but has calendar_dates.txt
        var zipOverrides = new Dictionary<string, string>
        {
            ["calendar.txt"] = null!, // Omit
            ["shapes.txt"] = null!, // Omit
            ["calendar_dates.txt"] = "service_id,date,exception_type\nWD,20260101,1" // Provide alternative required file
        };
        var zipData = MinimalGtfsZipBuilder.Build(zipOverrides);

        var client = CreateClient(zipData);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        // 3. Act - Run import
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // 4. Assert - Old data should be gone!
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var calendars = await db.GtfsCalendars.ToListAsync();
            var shapes = await db.GtfsShapePoints.ToListAsync();
            var calendarDates = await db.GtfsCalendarDates.ToListAsync();

            calendars.Should().BeEmpty("Because calendar.txt was missing from ZIP, the target table should be truncated");
            shapes.Should().BeEmpty("Because shapes.txt was missing from ZIP, the target table should be truncated");
            calendarDates.Should().NotBeEmpty("Because calendar_dates.txt was provided in the ZIP");
        }
    }

    [Fact]
    public async Task ImportGtfs_ThrowsException_RollbacksData_ButSavesFailedStatus()
    {
        // 1. Arrange - Seed an old "Completed" run and some data
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GtfsImportRuns.Add(new GtfsImportRun
            {
                Status = "Completed",
                IsActive = true,
                FileHash = "OLD_HASH",
                StartedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow,
                DownloadedAt = DateTime.UtcNow,
                SourceUrl = "test"
            });
            db.GtfsAgencies.Add(new GtfsAgency { AgencyId = "OLD_AGENCY", AgencyName = "Test", AgencyTimezone = "TR" });
            db.SaveChanges();
        }

        // 2. Build ZIP with malformed CSV for routes to trigger CsvHelper exception INSIDE the transaction
        var zipOverrides = new Dictionary<string, string>
        {
            ["routes.txt"] = "route_id,invalid_column\nR1,X" // Missing required columns
        };
        var zipData = MinimalGtfsZipBuilder.Build(zipOverrides);

        var client = CreateClient(zipData);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        // 3. Act - Run import (will return 500 or fail internally)
        var response = await client.SendAsync(request);

        // 4. Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Check if the old active run is still active (not overwritten or broken)
            var activeRuns = await db.GtfsImportRuns.Where(r => r.IsActive).ToListAsync();
            activeRuns.Should().HaveCount(1);
            activeRuns.Single().FileHash.Should().Be("OLD_HASH");
            
            // Check if the new run was saved as Failed
            var newRun = await db.GtfsImportRuns.OrderByDescending(r => r.Id).FirstAsync();
            newRun.Status.Should().Be("Failed");
            newRun.ErrorMessage.Should().Contain("beklenmeyen bir hata oluştu");
            newRun.FinishedAt.Should().NotBeNull();
            
            // Check if rollback worked! (The OLD_AGENCY should still be there because transaction rolled back the Truncate!)
            var agencies = await db.GtfsAgencies.ToListAsync();
            agencies.Should().ContainSingle(a => a.AgencyId == "OLD_AGENCY");
        }
    }
}
