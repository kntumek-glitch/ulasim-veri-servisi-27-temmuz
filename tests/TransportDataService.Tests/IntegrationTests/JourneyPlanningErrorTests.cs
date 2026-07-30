using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransportDataService;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace TransportDataService.Tests.IntegrationTests;

public class JourneyPlanningErrorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JourneyPlanningErrorTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InvalidCoordinates_ShouldReturn_400BadRequest()
    {
        var client = _factory.CreateClient();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 900, Lon = 27.0 }, // Invalid latitude
            Destination = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            DepartureDateTime = DateTimeOffset.UtcNow
        };
        
        // Wait, max validation on OriginLat is probably not defined in CoordinateDto, but let's see. 
        // If it isn't, we can test ArgumentException from inside the service.
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        
        // Either 400 from DataAnnotations or 400 from ArgumentException in Middleware
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var error = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        error.Should().NotBeNull();
        error!.Status.Should().Be(400);
    }

    [Fact]
    public async Task NoActiveFeed_ShouldReturn_404NotFound()
    {
        // We will override DbContext to use an empty one
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext>));
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase("EmptyDbFor404Test");
                });
            });
        });

        var client = customFactory.CreateClient();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4, Lon = 27.0 },
            Destination = new CoordinateDto { Lat = 38.4, Lon = 27.1 },
            DepartureDateTime = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var error = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        error.Should().NotBeNull();
        error!.Status.Should().Be(404);
        error.Title.Should().Be("Aktif GTFS Verisi Bulunamadı");
    }

    [Fact]
    public async Task ValidSearch_ButNoRouteFound_ShouldReturn_200OK_With_ReasonCode()
    {
        // Mock a DB with an active run but no trips for the searched coordinates
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext>));
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase("DbWithActiveFeedButNoStops");
                });
            });
        });

        using (var scope = customFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GtfsImportRuns.Add(new Domain.GtfsImportRun { Id = 999, FileHash = "HASH", IsActive = true, Status = "Completed", StartedAt = DateTime.UtcNow });
            db.SaveChanges();
        }

        var client = customFactory.CreateClient();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 0, Lon = 0 }, // Ocean coordinates
            Destination = new CoordinateDto { Lat = 1, Lon = 1 },
            DepartureDateTime = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<JourneyPlanSearchResponse>();
        result.Should().NotBeNull();
        result!.Itineraries.Should().BeEmpty();
        result.ReasonCode.Should().Be("NO_ROUTE_FOUND");
    }
}
