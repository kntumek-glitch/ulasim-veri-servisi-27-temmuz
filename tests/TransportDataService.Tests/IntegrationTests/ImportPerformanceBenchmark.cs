using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TransportDataService;
using Xunit;
using Xunit.Abstractions;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class ImportPerformanceBenchmark : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public ImportPerformanceBenchmark(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var activeRuns = await db.GtfsImportRuns.Where(r => r.IsActive).ToListAsync();
        foreach (var r in activeRuns) r.IsActive = false;
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClient(byte[]? zipData)
    {
        var testId = Guid.NewGuid().ToString();
        if (zipData != null) GtfsImportLifecycleTests.ZipDataStore[testId] = zipData;

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<ulasım_veri_servisi.Services.IGtfsImportService, ulasım_veri_servisi.Services.GtfsImportService>(c =>
                {
                    c.DefaultRequestHeaders.Add("X-Test-Id", testId);
                    c.DefaultRequestHeaders.Add("X-Test-StatusCode", HttpStatusCode.OK.ToString());
                })
                .ConfigurePrimaryHttpMessageHandler(() => new GtfsImportLifecycleTests.MockHttpMessageHandler());
            });
        }).CreateClient();
    }

    [Fact]
    public async Task RunPerformanceBenchmark_WithLargeMockData()
    {
        // Temiz bir veritabanı ile başla
        using (var initScope = _factory.Services.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"GtfsImportRuns\" CASCADE;");
        }

        var stopTimesBuilder = new StringBuilder();
        stopTimesBuilder.AppendLine("trip_id,arrival_time,departure_time,stop_id,stop_sequence,timepoint");
        for (int i = 1; i <= 50000; i++)
        {
            int stopIndex = (i % 10) + 1;
            stopTimesBuilder.AppendLine($"T1,08:00:00,08:01:00,S{stopIndex},{i},0");
        }

        var shapesBuilder = new StringBuilder();
        shapesBuilder.AppendLine("shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence,shape_dist_traveled");
        for (int i = 1; i <= 10000; i++)
        {
            shapesBuilder.AppendLine($"SH1,38.0,27.0,{i},0");
        }

        var zipBytes = TransportDataService.Tests.Helpers.MinimalGtfsZipBuilder.Build(new Dictionary<string, string>
        {
            ["stop_times.txt"] = stopTimesBuilder.ToString(),
            ["shapes.txt"] = shapesBuilder.ToString()
        });

        _output.WriteLine($"Generated Mock GTFS Zip Size: {zipBytes.Length / 1024.0 / 1024.0:F2} MB");

        // 2. Prepare HTTP Client
        var client = CreateClient(zipBytes);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        // 3. Track metrics
        long startMemory = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();

        // 4. Send Request
        var response = await client.SendAsync(request);
        
        sw.Stop();
        long endMemory = GC.GetTotalMemory(false);

        // 5. Output Results
        _output.WriteLine($"--- BENCHMARK RESULTS ---");
        _output.WriteLine($"Status Code: {response.StatusCode}");
        _output.WriteLine($"Time Elapsed: {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"Memory Start: {startMemory / 1024 / 1024} MB");
        _output.WriteLine($"Memory End:   {endMemory / 1024 / 1024} MB");
        _output.WriteLine($"Memory Diff:  {(endMemory - startMemory) / 1024 / 1024} MB");

        if (response.StatusCode != System.Net.HttpStatusCode.Created)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new Exception($"Benchmark failed with {response.StatusCode}: {content}");
        }

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var count = await context.GtfsStopTimes.CountAsync();
        _output.WriteLine($"Total StopTimes Imported: {count}");
        count.Should().Be(50000); // 50K StopTimes
    }
}
