using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TransportDataService;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("transport_test_db")
        .WithUsername("postgres")
        .WithPassword("postgres123")
        .Build();

    public string ConnectionString => _dbContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync().AsTask();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AdminSettings:ApiKey", "test-key"),
                new KeyValuePair<string, string>("JourneyPlan:MaxWalkingMeters", "10000")
            }!);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString));

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();

            // Mock OSRM to never make real HTTP requests during tests
            var routeProviderDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ulasim_veri_servisi.Services.IWalkingRouteProvider));
            if (routeProviderDescriptor != null) services.Remove(routeProviderDescriptor);
            services.AddSingleton<ulasim_veri_servisi.Services.IWalkingRouteProvider, MockWalkingRouteProvider>();
        });
    }
}

public class MockWalkingRouteProvider : ulasim_veri_servisi.Services.IWalkingRouteProvider
{
    public Task<ulasim_veri_servisi.Models.Gtfs.JourneyPlan.WalkingResult> GetWalkingRouteAsync(double srcLat, double srcLon, double tgtLat, double tgtLon, bool returnGeometry = false, string profile = "foot", CancellationToken cancellationToken = default)
    {
        // Simple Haversine approximation to maintain backwards compatibility with older tests
        double r = 6371e3;
        double p1 = srcLat * Math.PI / 180;
        double p2 = tgtLat * Math.PI / 180;
        double dp = (tgtLat - srcLat) * Math.PI / 180;
        double dl = (tgtLon - srcLon) * Math.PI / 180;

        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                   Math.Cos(p1) * Math.Cos(p2) *
                   Math.Sin(dl / 2) * Math.Sin(dl / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        double dist = r * c;
        return Task.FromResult(new ulasim_veri_servisi.Models.Gtfs.JourneyPlan.WalkingResult
        {
            State = new ulasim_veri_servisi.Models.Gtfs.JourneyPlan.ErrorState { IsSuccess = true },
            DistanceMeters = dist,
            DurationSeconds = dist / 1.2
        });
    }
}

[CollectionDefinition("IntegrationTestCollection")]
public class IntegrationTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
