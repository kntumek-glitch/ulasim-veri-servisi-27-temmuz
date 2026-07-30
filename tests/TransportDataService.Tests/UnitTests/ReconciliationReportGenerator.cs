using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

public class ReconciliationReportGenerator
{
    [Fact]
    public async Task GenerateReport_WithAll8Scenarios()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        
        var serviceProvider = services.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<AppDbContext>();

        // 1. Exact Matches (Doğrudan eşleşenler)
        context.Stops.Add(new Stop { ExternalStopId = "10001", Name = "Exact Match Stop", Latitude = 38.1, Longitude = 27.1 });
        context.GtfsStops.Add(new GtfsStop { StopId = "10001", StopCode = "C1", StopName = "Exact Match Stop", StopLat = 38.1, StopLon = 27.1 });

        // 2. StopIdMatchesOnly (Yalnızca Stop ID ile eşleşenler) - Name mismatch
        context.Stops.Add(new Stop { ExternalStopId = "10002", Name = "Old Name Stop", Latitude = 38.2, Longitude = 27.2 });
        context.GtfsStops.Add(new GtfsStop { StopId = "10002", StopCode = "C2", StopName = "New Name Stop", StopLat = 38.2, StopLon = 27.2 });

        // 3. StopCodeMatchesOnly (Yalnızca Stop Code ile eşleşenler) - ID doesn't match, Code matches
        context.Stops.Add(new Stop { ExternalStopId = "C3", Name = "Code Match Stop", Latitude = 38.3, Longitude = 27.3 });
        context.GtfsStops.Add(new GtfsStop { StopId = "10003", StopCode = "C3", StopName = "Code Match Stop", StopLat = 38.3, StopLon = 27.3 });

        // 4. OnlyInGtfs (Yalnızca GTFS'te bulunanlar) - Not in stops, no similar name
        context.GtfsStops.Add(new GtfsStop { StopId = "10004", StopCode = "C4", StopName = "Brand New Stop", StopLat = 38.4, StopLon = 27.4 });

        // 5. OnlyInStops (Yalnızca eski Stops tablosunda bulunanlar) - Not in gtfs
        context.Stops.Add(new Stop { ExternalStopId = "10005", Name = "Abandoned Stop", Latitude = 38.5, Longitude = 27.5 });

        // 6. NameMismatches (İsim farkı bulunanlar) - Same as 2 for counting, but let's add one more
        context.Stops.Add(new Stop { ExternalStopId = "10006", Name = "Typo Stop A", Latitude = 38.6, Longitude = 27.6 });
        context.GtfsStops.Add(new GtfsStop { StopId = "10006", StopCode = "C6", StopName = "Typo Stop B", StopLat = 38.6, StopLon = 27.6 });

        // 7. CoordinateMismatches (Koordinat farkı bulunanlar) - ID matches but coords are way off
        context.Stops.Add(new Stop { ExternalStopId = "10007", Name = "Moved Stop", Latitude = 38.7, Longitude = 27.7 });
        context.GtfsStops.Add(new GtfsStop { StopId = "10007", StopCode = "C7", StopName = "Moved Stop", StopLat = 38.8, StopLon = 27.8 });

        // 8. ManualReview (Manuel inceleme gerekenler) - Missing in stops by ID/Code, but name exactly matches an existing stop that wasn't matched!
        // To trigger this, we need an old stop with a weird ExternalStopId, and a GTFS stop with the exact same name but different ID and Code.
        context.Stops.Add(new Stop { ExternalStopId = "OLD-999", Name = "Manual Review Stop", Latitude = 38.9, Longitude = 27.9 });
        context.GtfsStops.Add(new GtfsStop { StopId = "NEW-999", StopCode = "C999", StopName = "Manual Review Stop", StopLat = 38.9, StopLon = 27.9 });

        await context.SaveChangesAsync();

        var service = new GtfsStopReconciliationService(context);
        var result = await service.ReconcileAsync(CancellationToken.None);

        // This will write to docs/gtfs-stop-reconciliation.md relative to the test runner's current directory (e.g. tests/TransportDataService.Tests/bin/Debug/net8.0/docs/...)
        // We can just print the exact path so we can copy it later.
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "gtfs-stop-reconciliation.md");
        Console.WriteLine($"Report generated at: {reportPath}");
        Assert.NotNull(result);
    }
}

