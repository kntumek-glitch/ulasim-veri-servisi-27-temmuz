using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Models.Gtfs;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class ReconciliationIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly AppDbContext _context;

    public ReconciliationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();

        var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed test data for reconciliation
        SeedReconciliationDataAsync().GetAwaiter().GetResult();
    }

    private async Task SeedReconciliationDataAsync()
    {
        // Clear existing data
        await _context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "Stops", "GtfsStops", "GtfsImportRuns" RESTART IDENTITY CASCADE
            """);

        var run = new GtfsImportRun { Id = 1, Status = "Completed", IsActive = true, FinishedAt = DateTime.UtcNow };
        _context.GtfsImportRuns.Add(run);

        // Stops table (CSV import data)
        var stops = new List<Stop>
        {
            new() { Id = 1, ExternalStopId = "S1", Name = "Stop 1", Latitude = 38.4, Longitude = 27.1 },
            new() { Id = 2, ExternalStopId = "S2", Name = "Stop 2", Latitude = 38.5, Longitude = 27.2 },
            new() { Id = 3, ExternalStopId = "S3", Name = "Stop 3", Latitude = 38.6, Longitude = 27.3 },
            new() { Id = 4, ExternalStopId = "S4", Name = "Stop 4", Latitude = 38.7, Longitude = 27.4 },
            new() { Id = 5, ExternalStopId = "S5", Name = "Stop 5", Latitude = 38.8, Longitude = 27.5 },
            new() { Id = 6, ExternalStopId = "S6", Name = "Stop 6", Latitude = 38.9, Longitude = 27.6 },
        };

        // GTFS Stops (from GTFS import)
        var gtfsStops = new List<GtfsStop>
        {
            new() { GtfsImportRunId = 1, Id = 1, StopId = "S1", StopName = "Stop 1", StopLat = 38.4, StopLon = 27.1, StopCode = "S1" },      // Direct match
            new() { GtfsImportRunId = 1, Id = 2, StopId = "S2", StopName = "Stop 2", StopLat = 38.5, StopLon = 27.2, StopCode = "S2" },      // Direct match
            new() { GtfsImportRunId = 1, Id = 3, StopId = "S3", StopName = "Stop 3 Modified", StopLat = 38.6, StopLon = 27.3, StopCode = "S3" }, // Name mismatch
            new() { GtfsImportRunId = 1, Id = 4, StopId = "S4", StopName = "Stop 4", StopLat = 38.7002, StopLon = 27.4002, StopCode = "S4" }, // Coordinate mismatch
            new() { GtfsImportRunId = 1, Id = 5, StopId = "S7", StopName = "Stop 7", StopLat = 38.9, StopLon = 27.7, StopCode = "S7" },      // Missing in Stops
            new() { GtfsImportRunId = 1, Id = 6, StopId = "S8", StopName = "Stop 8", StopLat = 39.0, StopLon = 27.8, StopCode = "S8" },      // Missing in Stops
        };

        _context.Stops.AddRange(stops);
        _context.GtfsStops.AddRange(gtfsStops);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Reconcile_GeneratesReportWithCorrectCounts()
    {
        // Act - Call the reconciliation endpoint
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reconciliation/gtfs-stops");
        request.Headers.Add("X-Admin-Key", "test-key");
        var response = await _client.SendAsync(request);

        // Assert
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ReconciliationResult>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        result.Should().NotBeNull();
        result!.ExactMatches.Should().Be(2);       // S1, S2
        result.StopCodeMatchesOnly.Should().Be(0);     
        result.OnlyInGtfs.Should().Be(2);      // S7, S8
        result.OnlyInStops.Should().Be(2);       // S5, S6
        result.NameMismatches.Should().Be(1);      // S3
        result.CoordinateMismatches.Should().Be(1); // S4
        result.ManualReview.Should().Be(0);
    }

    [Fact]
    public async Task Reconcile_GeneratesMarkdownReportFile()
    {
        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reconciliation/gtfs-stops");
        request.Headers.Add("X-Admin-Key", "test-key");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // Assert - Check report file exists
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "gtfs-stop-reconciliation.md");
        File.Exists(reportPath).Should().BeTrue();

        var reportContent = await File.ReadAllTextAsync(reportPath);
        reportContent.Should().Contain("# GTFS Stop Reconciliation");
        reportContent.Should().Contain("e\u015fle\u015fenler"); // Matches "Doğrudan eşleşenler" or similar (UTF8 safe)
        reportContent.Should().Contain("2"); // matches count
    }
}

public class ReconciliationResult
{
    public int ExactMatches { get; set; }
    public int StopIdMatchesOnly { get; set; }
    public int StopCodeMatchesOnly { get; set; }
    public int OnlyInGtfs { get; set; }
    public int OnlyInStops { get; set; }
    public int NameMismatches { get; set; }
    public int CoordinateMismatches { get; set; }
    public int ManualReview { get; set; }
}
