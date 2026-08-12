using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class ApiSecurityReliabilityTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public ApiSecurityReliabilityTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task Concurrency_LoadTest_50ConcurrentRequests_ShouldCompleteSuccessfully()
    {
        // 1. Concurrency: 50 concurrent requests (Load/Stress test) using Task.WhenAll
        // Arrange
        var client = _factory.CreateClient();
        var requestCount = 50;
        var tasks = new List<Task<HttpResponseMessage>>();
        
        var requestPayload = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4237, Lon = 27.1428 },
            Destination = new CoordinateDto { Lat = 38.4593, Lon = 27.2185 },
            DepartureDateTime = DateTime.Now.AddHours(1)
        };

        // Act
        for (int i = 0; i < requestCount; i++)
        {
            tasks.Add(client.PostAsJsonAsync("/api/v1/journey-plans/search", requestPayload));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            Assert.True(response.IsSuccessStatusCode, $"Request failed with status {response.StatusCode}");
        }
    }

    [Fact]
    public async Task AbortHandling_ClientCancellation_ShouldThrowTaskCanceledException()
    {
        // 2. Abort Handling: Client cancellation (CancellationToken propagation)
        // Arrange
        var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.GetAsync("/api/v1/gtfs/agencies", cts.Token);
        });
    }

    [Fact]
    public async Task AbortHandling_SearchTimeout_ShouldReturn408RequestTimeout()
    {
        // 3. Abort Handling: Search timeout (Engine execution timeout).
        // Arrange: Configure MaxSearchTimeSeconds to 0 so it times out immediately
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>("JourneyPlan:MaxSearchTimeSeconds", "0")
                }!);
            });
        });
        
        var client = factory.CreateClient();

        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4237, Lon = 27.1428 },
            Destination = new CoordinateDto { Lat = 38.4593, Lon = 27.2185 },
            DepartureDateTime = DateTime.Now.AddHours(1)
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Assert
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("SEARCH_TIMEOUT", content);
    }

    [Fact]
    public async Task Security_InternalExceptionDetailLeakPrevention_ShouldNotLeakStackTrace()
    {
        // 4. Security: Internal exception detail leak prevention (Masking/Sanitization).
        // Arrange: Mock the service to throw a raw Exception
        var mockService = new Mock<IJourneyPlanningService>();
        mockService.Setup(s => s.SearchJourneyAsync(It.IsAny<JourneyPlanSearchRequest>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new Exception("SUPER_SECRET_STACK_TRACE_DO_NOT_LEAK"));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockService.Object);
            });
        });

        var client = factory.CreateClient();
        
        var request = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4237, Lon = 27.1428 },
            Destination = new CoordinateDto { Lat = 38.4593, Lon = 27.2185 },
            DepartureDateTime = DateTime.Now.AddHours(1)
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/journey-plans/search", request);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        
        // Ensure standard ProblemDetails format
        Assert.Contains("INTERNAL_ERROR", content);
        // Ensure no sensitive leak
        Assert.DoesNotContain("SUPER_SECRET_STACK_TRACE_DO_NOT_LEAK", content);
        Assert.DoesNotContain("System.Exception", content);
        Assert.DoesNotContain("at ", content); // rough check for stack trace
    }

    [Fact]
    public async Task Security_RateLimitingEnforcement_ShouldReturn429TooManyRequests()
    {
        // 5. Security: Rate limiting enforcement (HTTP 429). Send many requests in a loop.
        // Arrange
        // The policy allows 50 requests per 10 seconds. We send 60 to guarantee a 429.
        var client = _factory.CreateClient();
        
        var requestPayload = new JourneyPlanSearchRequest
        {
            Origin = new CoordinateDto { Lat = 38.4237, Lon = 27.1428 },
            Destination = new CoordinateDto { Lat = 38.4593, Lon = 27.2185 },
            DepartureDateTime = DateTime.Now.AddHours(1)
        };

        var tasks = new List<Task<HttpResponseMessage>>();
        
        // Act: Send 60 requests quickly
        for (int i = 0; i < 60; i++)
        {
            tasks.Add(client.PostAsJsonAsync("/api/v1/journey-plans/search", requestPayload));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        var has429 = responses.Any(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(has429, "Expected at least one request to be rate-limited (429 Too Many Requests).");
    }

    [Fact]
    public async Task Security_ProductionCorsPolicy_ShouldRejectDisallowedOrigins()
    {
        // 6. Security: Production CORS policy validation (Allowed vs. Disallowed origins).
        // Arrange: Configure environment to Production and set AllowedOrigins
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>("AllowedOrigins:0", "https://trusted-domain.com")
                }!);
            });
        });
        
        var client = factory.CreateClient();

        // Act 1: Disallowed Origin
        var disallowedRequest = new HttpRequestMessage(HttpMethod.Options, "/api/v1/gtfs/agencies");
        disallowedRequest.Headers.Add("Origin", "https://evil-hacker.com");
        disallowedRequest.Headers.Add("Access-Control-Request-Method", "GET");

        var disallowedResponse = await client.SendAsync(disallowedRequest);

        // Assert 1
        Assert.False(disallowedResponse.Headers.Contains("Access-Control-Allow-Origin"), "Disallowed origin should not have CORS headers.");

        // Act 2: Allowed Origin
        var allowedRequest = new HttpRequestMessage(HttpMethod.Options, "/api/v1/gtfs/agencies");
        allowedRequest.Headers.Add("Origin", "https://trusted-domain.com");
        allowedRequest.Headers.Add("Access-Control-Request-Method", "GET");

        var allowedResponse = await client.SendAsync(allowedRequest);

        // Assert 2
        Assert.True(allowedResponse.Headers.Contains("Access-Control-Allow-Origin"), "Allowed origin should have CORS headers.");
        Assert.Equal("https://trusted-domain.com", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").First());
    }
}

