using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

[Collection("PostgreSql collection")]
public class GtfsStopReconciliationServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _context = null!;
    private GtfsStopReconciliationService _service = null!;
    private readonly string _reportPath;

    public GtfsStopReconciliationServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _reportPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "gtfs-stop-reconciliation.md");
    }

    public async Task InitializeAsync()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString).Options);
        await _context.Database.MigrateAsync();
        await _context.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "Stops", "GtfsStops" RESTART IDENTITY CASCADE""");

        _context.Stops.AddRange(
            new Stop { ExternalStopId = "S1", Name = "Stop One", Latitude = 38.4, Longitude = 27.1 },
            new Stop { ExternalStopId = "S2", Name = "Stop Two", Latitude = 38.5, Longitude = 27.2 },
            new Stop { ExternalStopId = "S9", Name = "Only In Stops", Latitude = 38.6, Longitude = 27.3 });
        _context.GtfsStops.AddRange(
            new GtfsStop { StopId = "S1", StopName = "Stop One", StopLat = 38.4, StopLon = 27.1 },
            new GtfsStop { StopId = "S2", StopName = "Different Name", StopLat = 38.5, StopLon = 27.2 },
            new GtfsStop { StopId = "G9", StopName = "Only In GTFS", StopLat = 38.7, StopLon = 27.4 });
        await _context.SaveChangesAsync();

        _service = new GtfsStopReconciliationService(_context);
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        if (File.Exists(_reportPath)) File.Delete(_reportPath);
        return Task.CompletedTask;
    }

    private static int ExtractCount(string report, string section) =>
        int.Parse(Regex.Match(report, $@"## {section}\s+\n\s+(\d+)").Groups[1].Value);

    [Fact]
    public async Task ReconcileAsync_WritesExpectedCounts()
    {
        await _service.ReconcileAsync(CancellationToken.None);

        var report = await File.ReadAllTextAsync(_reportPath);
        ExtractCount(report, "Direct Matches").Should().Be(2);
        ExtractCount(report, "StopId Matches").Should().Be(2);
        ExtractCount(report, "Missing In Stops").Should().Be(1);
        ExtractCount(report, "Missing In GTFS").Should().Be(1);
        ExtractCount(report, "Name Mismatch").Should().Be(1);
    }
}
