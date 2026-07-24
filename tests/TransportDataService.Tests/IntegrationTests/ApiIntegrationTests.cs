using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Models.Gtfs;
using Xunit;
using Testcontainers.PostgreSql;

namespace TransportDataService.Tests.IntegrationTests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = default!;

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("transport_test_db")
        .WithUsername("postgres")
        .WithPassword("postgres123")
        .Build();

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        _client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Add Npgsql DbContext
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseNpgsql(_dbContainer.GetConnectionString());
                });

                // Build the service provider and seed data
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
                
                SeedTestData(db);
            });
        }).CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync().AsTask();
    }

    private void SeedTestData(AppDbContext db)
    {
        if (!db.GtfsRoutes.Any())
        {
            var route = new GtfsRoute { Id = 1, RouteId = "R1", RouteShortName = "TEST", RouteLongName = "Test Route" };
            var trip = new GtfsTrip { Id = 1, TripId = "T1", RouteId = "R1", DirectionId = 0, GtfsRouteId = 1 };
            var stop1 = new GtfsStop { Id = 1, StopId = "S1", StopName = "Stop 1" };
            var stop2 = new GtfsStop { Id = 2, StopId = "S2", StopName = "Stop 2" };
            var st1 = new GtfsStopTime { Id = 1, GtfsTripId = 1, GtfsStopId = 1, StopSequence = 10 };
            var st2 = new GtfsStopTime { Id = 2, GtfsTripId = 1, GtfsStopId = 2, StopSequence = 20 };

            db.GtfsRoutes.Add(route);
            db.GtfsTrips.Add(trip);
            db.GtfsStops.AddRange(stop1, stop2);
            db.GtfsStopTimes.AddRange(st1, st2);
            
            db.SaveChanges();
        }
    }

    [Fact]
    public async Task GetRoutes_ReturnsPaginatedResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/gtfs/routes?page=1&pageSize=10");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<PaginatedResponse<RouteDto>>(content, options);

        result.Should().NotBeNull();
        result!.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        result.Items.Should().Contain(r => r.RouteId == "R1");
    }

    [Fact]
    public async Task GetRouteStops_ValidRoute_ReturnsOrderedStops()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/gtfs/routes/R1/stops?directionId=0");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var stops = JsonSerializer.Deserialize<List<RouteStopDto>>(content, options);

        stops.Should().NotBeNull();
        stops.Should().HaveCount(2);
        stops![0].StopSequence.Should().Be(10);
        stops[1].StopSequence.Should().Be(20);
    }

    [Fact]
    public async Task GetRouteStops_InvalidRouteId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/gtfs/routes/INVALID_ROUTE/stops");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidParameters_ReturnsProblemDetails()
    {
        // Act - Trigger validation error by passing invalid parameter type if possible, 
        // or let's test a non-existent endpoint to check if standard error handling works.
        // E.g., page size is invalid but it corrects it. 
        // We can test a completely invalid stop endpoint that throws an exception or uses bad params.
        var response = await _client.GetAsync("/api/v1/gtfs/routes/R1/stops?directionId=invalid_int");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        
        // ASP.NET Core should return ProblemDetails by default
        content.Should().Contain("\"title\"");
        content.Should().Contain("\"status\"");
        content.Should().Contain("\"traceId\"");
    }

    [Fact]
    public async Task GetStops_InvalidPage_ReturnsProblemDetails400()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/stops?page=-1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var problem = JsonSerializer.Deserialize<ProblemDetails>(content, options);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Geçersiz parametre");
        problem.Detail.Should().Be("page değeri en az 1 olmalıdır.");
        problem.Status.Should().Be(400);
    }

    [Fact]
    public async Task GetStopById_InvalidId_ReturnsProblemDetails404()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/stops/9999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var content = await response.Content.ReadAsStringAsync();
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var problem = JsonSerializer.Deserialize<ProblemDetails>(content, options);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Kaynak bulunamadı");
        problem.Detail.Should().Be("İstenen kaynak bulunamadı.");
        problem.Status.Should().Be(404);
    }
}
