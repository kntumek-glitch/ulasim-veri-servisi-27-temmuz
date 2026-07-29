using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasım_veri_servisi.Models.Gtfs;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class ApiIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        SeedTestData(db);
    }

    private void SeedTestData(AppDbContext db)
    {
        if (!db.GtfsRoutes.Any())
        {
            var run = db.GtfsImportRuns.FirstOrDefault(r => r.IsActive);
            if (run == null)
            {
                run = new GtfsImportRun { FileHash = "test", Status = "Completed", IsActive = true };
                db.GtfsImportRuns.Add(run);
            }
            
            var route = new GtfsRoute { GtfsImportRun = run, RouteId = "R1", RouteShortName = "TEST", RouteLongName = "Test Route" };
            var trip = new GtfsTrip { GtfsImportRun = run, TripId = "T1", RouteId = "R1", DirectionId = 0, Route = route };
            var stop1 = new GtfsStop { GtfsImportRun = run, StopId = "S1", StopName = "Stop 1" };
            var stop2 = new GtfsStop { GtfsImportRun = run, StopId = "S2", StopName = "Stop 2" };
            var st1 = new GtfsStopTime { GtfsImportRun = run, Trip = trip, Stop = stop1, StopSequence = 10 };
            var st2 = new GtfsStopTime { GtfsImportRun = run, Trip = trip, Stop = stop2, StopSequence = 20 };

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
        var response = await _client.GetAsync("/api/v1/gtfs/routes?page=1&pageSize=10");
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
        var response = await _client.GetAsync("/api/v1/gtfs/routes/R1/stops?directionId=0");
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
        var response = await _client.GetAsync("/api/v1/gtfs/routes/INVALID_ROUTE/stops");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidParameters_ReturnsProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/gtfs/routes/R1/stops?directionId=invalid_int");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        
        content.Should().Contain("\"title\"");
        content.Should().Contain("\"status\"");
        content.Should().Contain("\"traceId\"");
    }

    [Fact]
    public async Task GetStops_InvalidPage_ReturnsProblemDetails400()
    {
        var response = await _client.GetAsync("/api/v1/stops?page=-1");
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
        var response = await _client.GetAsync("/api/v1/stops/9999");
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
