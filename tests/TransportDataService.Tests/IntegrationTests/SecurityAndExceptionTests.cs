using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using TransportDataService;
using Xunit;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using ulasim_veri_servisi.Services;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("IntegrationTestCollection")]
public class SecurityAndExceptionTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SecurityAndExceptionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task ImportGtfs_WithoutAdminKey_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync("/api/v1/import/gtfs", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportGtfs_WithInvalidAdminKey_ReturnsForbidden()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "invalid_key");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnknownEndpoint_ReturnsProblemDetails_WithoutStackTrace()
    {
        var response = await _client.GetAsync("/api/v1/some-unknown-endpoint-that-does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("StackTrace");
        content.Should().NotContain("Exception");
    }

    [Fact]
    public async Task ImportGtfs_WithValidKey_DoesNotReturnUnauthorized()
    {
        // Our CustomWebApplicationFactory uses "test-key"
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");
        
        var response = await _client.SendAsync(request);
        
        // It might be 400 or 500 depending on actual file, but it should NOT be 401 or 403
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ImportGtfs_WhenUnexpectedExceptionOccurs_DoesNotLeakStackTraceOrSensitiveDetails()
    {
        // Arrange: Replace the real service with a mock that throws an exception containing sensitive details
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IGtfsImportService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                var mockService = new Mock<IGtfsImportService>();
                var sensitiveException = new Exception("SECRET_SQL_DETAILS: SELECT * FROM Users; File: C:\\secret\\app\\secrets.json\n   at SomeMethod() in path\\file.cs:line 50");
                mockService.Setup(x => x.ImportAsync(It.IsAny<CancellationToken>())).ThrowsAsync(sensitiveException);
                
                services.AddScoped<IGtfsImportService>(_ => mockService.Object);
            });
        }).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/gtfs");
        request.Headers.Add("X-Admin-Key", "test-key");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var content = await response.Content.ReadAsStringAsync();
        
        // Ensure no sensitive information is leaked
        content.Should().NotContain("SECRET_SQL_DETAILS");
        content.Should().NotContain("SELECT * FROM Users");
        content.Should().NotContain("C:\\secret\\app\\secrets.json");
        content.Should().NotContain("at SomeMethod()");
        content.Should().NotContain("line 50");
        
        // Ensure the generic message is present
        content.Should().Contain("Beklenmeyen bir sunucu hatası oluştu.");
    }
}

