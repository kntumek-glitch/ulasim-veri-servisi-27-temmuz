using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Services;
using Xunit;
using TransportDataService.Tests.IntegrationTests;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class GtfsImportRetentionTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public GtfsImportRetentionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        await db.GtfsImportRuns.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CleanupOldFeedsAsync_AppliesRetentionRulesCorrectly()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var importService = scope.ServiceProvider.GetRequiredService<IGtfsImportService>();

        // Create mock runs
        var runs = new List<GtfsImportRun>
        {
            // Older Completed (Should be deleted because we only keep 2)
            new GtfsImportRun { Status = "Completed", FileHash = "hash1", StartedAt = DateTime.UtcNow.AddDays(-20), FinishedAt = DateTime.UtcNow.AddDays(-20), IsActive = false },
            
            // Recent Completed 1 (Kept)
            new GtfsImportRun { Status = "Completed", FileHash = "hash2", StartedAt = DateTime.UtcNow.AddDays(-5), FinishedAt = DateTime.UtcNow.AddDays(-5), IsActive = false },
            
            // Recent Completed 2 (Kept)
            new GtfsImportRun { Status = "Completed", FileHash = "hash3", StartedAt = DateTime.UtcNow.AddDays(-4), FinishedAt = DateTime.UtcNow.AddDays(-4), IsActive = false },
            
            // Active Run (Kept)
            new GtfsImportRun { Status = "Completed", FileHash = "hash4", StartedAt = DateTime.UtcNow.AddDays(-3), FinishedAt = DateTime.UtcNow.AddDays(-3), IsActive = true },
            
            // Old Failed Run (Deleted because > 7 days)
            new GtfsImportRun { Status = "Failed", FileHash = "hash5", StartedAt = DateTime.UtcNow.AddDays(-10), FinishedAt = DateTime.UtcNow.AddDays(-10), IsActive = false },
            
            // Recent Cancelled Run (Kept because <= 7 days)
            new GtfsImportRun { Status = "Cancelled", FileHash = "hash6", StartedAt = DateTime.UtcNow.AddDays(-3), FinishedAt = DateTime.UtcNow.AddDays(-3), IsActive = false },
            
            // Running Run (Kept)
            new GtfsImportRun { Status = "Running", FileHash = "hash7", StartedAt = DateTime.UtcNow, IsActive = false }
        };

        db.GtfsImportRuns.AddRange(runs);
        await db.SaveChangesAsync();

        // Ensure foreign keys (phases) are also tracked for deletion
        var phase1 = new GtfsImportPhase { GtfsImportRunId = runs[0].Id, PhaseName = "Parsing", StartedAt = DateTime.UtcNow };
        var phase2 = new GtfsImportPhase { GtfsImportRunId = runs[4].Id, PhaseName = "Parsing", StartedAt = DateTime.UtcNow };
        var phase3 = new GtfsImportPhase { GtfsImportRunId = runs[5].Id, PhaseName = "Parsing", StartedAt = DateTime.UtcNow };
        db.GtfsImportPhases.AddRange(phase1, phase2, phase3);
        await db.SaveChangesAsync();

        // Act
        await importService.CleanupOldFeedsAsync(CancellationToken.None);

        // Assert
        var remainingRuns = await db.GtfsImportRuns.OrderBy(x => x.Id).ToListAsync();
        var remainingPhases = await db.GtfsImportPhases.ToListAsync();

        remainingRuns.Should().HaveCount(5, "only the old completed run and old failed run should be deleted.");
        
        // Verify kept runs
        remainingRuns.Should().Contain(x => x.FileHash == "hash2"); // Completed 1
        remainingRuns.Should().Contain(x => x.FileHash == "hash3"); // Completed 2
        remainingRuns.Should().Contain(x => x.FileHash == "hash4"); // Active
        remainingRuns.Should().Contain(x => x.FileHash == "hash6"); // Recent Cancelled
        remainingRuns.Should().Contain(x => x.FileHash == "hash7"); // Running

        // Verify deleted runs
        remainingRuns.Should().NotContain(x => x.FileHash == "hash1"); // Old Completed
        remainingRuns.Should().NotContain(x => x.FileHash == "hash5"); // Old Failed

        // Verify phases
        remainingPhases.Should().HaveCount(1, "phases for deleted runs should be cascade/explicitly deleted");
        remainingPhases.Single().GtfsImportRunId.Should().Be(runs[5].Id, "only phase for hash6 should be kept");
    }
}
