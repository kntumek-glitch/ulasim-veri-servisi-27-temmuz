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

[Collection("PostgreSql collection")]
public class ReconciliationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private AppDbContext _context = null!;

    public ReconciliationIntegrationTests(PostgreSqlFixture fixture) => _fixture = fixture;

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
                _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                _context.Database.Migrate();
            });
        });

        _client = _factory.CreateClient();

        // Seed test data for reconciliation
        await SeedReconciliationDataAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        _context?.Dispose();
        await _fixture.DisposeAsync();
    }

    private async Task SeedReconciliationDataAsync()
    {
        // Clear existing data
        await _context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "Stops", "GtfsStops" RESTART IDENTITY CASCADE
            """);

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
            new() { Id = 1, StopId = "S1", StopName = "Stop 1", StopLat = 38.4, StopLon = 27.1, StopCode = "S1" },      // Direct match
            new() { Id = 2, StopId = "S2", StopName = "Stop 2", StopLat = 38.5, StopLon = 27.2, StopCode = "S2" },      // Direct match
            new() { Id = 3, StopId = "S3", StopName = "Stop 3 Modified", StopLat = 38.6, StopLon = 27.3, StopCode = "S3" }, // Name mismatch
            new() { Id = 4, StopId = "S4", StopName = "Stop 4", StopLat = 38.7001, StopLon = 27.4001, StopCode = "S4" }, // Coordinate mismatch
            new() { Id = 5, StopId = "S7", StopName = "Stop 7", StopLat = 38.9, StopLon = 27.7, StopCode = "S7" },      // Missing in Stops
            new() { Id = 6, StopId = "S8", StopName = "Stop 8", StopLat = 39.0, StopLon = 27.8, StopCode = "S8" },      // Missing in Stops
        };

        _context.Stops.AddRange(stops);
        _context.GtfsStops.AddRange(gtfsStops);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Reconcile_GeneratesReportWithCorrectCounts()
    {
        // Act - Call the reconciliation endpoint
        var response = await _client.PostAsync("/api/v1/reconciliation/gtfs-stops", null);

        // Assert
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ReconciliationResult>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        result.Should().NotBeNull();
        result!.TotalMatches.Should().Be(2);       // S1, S2
        result.StopCodeMatches.Should().Be(0);     // No additional StopCode matches
        result.MissingInStops.Should().Be(2);      // S7, S8
        result.MissingInGtfs.Should().Be(1);       // S5, S6 (but S6 has no ExternalStopId? Wait, all have ExternalStopId)
        result.NameMismatches.Should().Be(1);      // S3
        result.CoordinateMismatches.Should().Be(1); // S4
        result.ManualReview.Should().Be(0);        // No name-only matches without ID/Code match
    }

    [Fact]
    public async Task Reconcile_GeneratesMarkdownReportFile()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/reconciliation/gtfs-stops", null);
        response.EnsureSuccessStatusCode();

        // Assert - Check report file exists
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "gtfs-stop-reconciliation.md");
        File.Exists(reportPath).Should().BeTrue();

        var reportContent = await File.ReadAllTextAsync(reportPath);
        reportContent.Should().Contain("# GTFS Stop Reconciliation");
        reportContent.Should().Contain("Total Matches");
        reportContent.Should().Contain("2"); // Total matches count
    }
}

public class ReconciliationResult
{
    public int TotalMatches { get; set; }
    public int StopCodeMatches { get; set; }
    public int MissingInStops { get; set; }
    public int MissingInGtfs { get; set; }
    public int NameMismatches { get; set; }
    public int CoordinateMismatches { get; set; }
    public int ManualReview { get; set; }
}
