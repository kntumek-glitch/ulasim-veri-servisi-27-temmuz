using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("Database collection")]
public class JourneyPlansIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public JourneyPlansIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_NoActiveFeed_Returns404NotFound_WithProblemDetails()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ensure there is no active feed in the database
        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var run in activeRuns)
        {
            run.IsActive = false;
        }
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = DateTimeOffset.UtcNow,
            MaxTransfers = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("Aktif GTFS Verisi Bulunamadı");
        problemDetails.Detail.Should().Contain("Sistemde işlem yapabilecek aktif bir GTFS veri seti bulunamadı");
    }

    [Fact]
    public async Task Search_WithActiveFeed_ReturnsCorrectMetadata()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Deactivate any existing active runs to avoid unique constraint violations
        var activeRuns = db.GtfsImportRuns.Where(r => r.IsActive).ToList();
        foreach (var activeRun in activeRuns)
        {
            activeRun.IsActive = false;
        }
        await db.SaveChangesAsync();

        // Make sure there is an active run
        var run = new GtfsImportRun
        {
            FileHash = "test-hash-journey-plan",
            Status = "Completed",
            IsActive = true,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow
        };
        db.GtfsImportRuns.Add(run);
        
        var agency = new GtfsAgency
        {
            AgencyId = "test-agency",
            AgencyName = "Test Agency",
            AgencyUrl = "http://test",
            AgencyTimezone = "Europe/Istanbul",
            GtfsImportRun = run
        };
        db.GtfsAgencies.Add(agency);
        
        var calendar = new GtfsCalendar
        {
            ServiceId = "service1",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            GtfsImportRun = run
        };
        db.GtfsCalendars.Add(calendar);
        
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            Destination = new CoordinateDto { Lat = 38.5, Lon = 27.2 },
            DepartureDateTime = DateTimeOffset.UtcNow,
            MaxTransfers = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        
        result.Should().NotBeNull();
        result!.Metadata.Should().NotBeNull();
        
        // Assert Metadata fields
        result.Metadata.ActiveImportId.Should().Be(run.Id);
        result.Metadata.FeedHash.Should().Be("test-hash-journey-plan");
        result.Metadata.Timezone.Should().Be("Europe/Istanbul");
        result.Metadata.StartDate.Should().Be(calendar.StartDate.ToString("yyyy-MM-dd"));
        result.Metadata.EndDate.Should().Be(calendar.EndDate.ToString("yyyy-MM-dd"));
        result.Metadata.IsStale.Should().BeFalse();
        result.Metadata.DataSourceWarning.Should().Contain("statik (planlı)");

        // Cleanup
        db.GtfsImportRuns.Remove(run); // cascades
        await db.SaveChangesAsync();
    }
}
