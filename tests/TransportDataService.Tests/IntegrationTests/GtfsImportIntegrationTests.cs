using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class GtfsImportIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public GtfsImportIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        
        public MockHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private HttpClient CreateMockedClient(HttpStatusCode externalStatusCode)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IGtfsImportService, GtfsImportService>()
                        .ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler(externalStatusCode));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Import_WhenExternalServiceReturns502_Returns502BadGateway()
    {
        var client = CreateMockedClient(HttpStatusCode.BadGateway);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        var content = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(502);
        problem.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Import_WhenExternalServiceReturns503_Returns502BadGateway()
    {
        // Even if the external service returns 503 Service Unavailable, our gateway
        // should log it and return 502 Bad Gateway (or 503 depending on design).
        // The previous test expected 502, so we enforce it here correctly.
        var client = CreateMockedClient(HttpStatusCode.ServiceUnavailable);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        var content = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(502);
        problem.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Import_WhenExternalServiceTimesOut_Returns503ServiceUnavailable()
    {
        // To simulate a timeout, we throw TaskCanceledException
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IGtfsImportService, GtfsImportService>()
                        .ConfigurePrimaryHttpMessageHandler(() => new ThrowingHttpMessageHandler(new TaskCanceledException("Timeout")));
            });
        }).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var content = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(503);
        problem.Title.Should().NotBeNullOrEmpty();
    }

    private class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ThrowingHttpMessageHandler(Exception exception) => _exception = exception;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _exception;
    }
}

