using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using ulasim_veri_servisi.Exceptions;
using ulasim_veri_servisi.Middleware;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

public class ExceptionMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionMiddleware>> _loggerMock;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExceptionMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<ExceptionMiddleware>>();
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_Returns404ProblemDetails()
    {
        // Arrange
        var middleware = new ExceptionMiddleware(
            next: (innerHttpContext) => throw new NotFoundException("Not found"),
            logger: _loggerMock.Object
        );

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.ContentType.Should().Be("application/problem+json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        
        var problem = JsonSerializer.Deserialize<ProblemDetails>(responseBody, _jsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Kaynak bulunamadı");
        problem.Detail.Should().Be("İstenen kaynak bulunamadı.");
        problem.Status.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_BadGatewayException_Returns502ProblemDetails()
    {
        // Arrange
        var middleware = new ExceptionMiddleware(
            next: (innerHttpContext) => throw new BadGatewayException("ESHOT Error"),
            logger: _loggerMock.Object
        );

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        
        var problem = JsonSerializer.Deserialize<ProblemDetails>(responseBody, _jsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Dış servis hatası");
        problem.Detail.Should().Be("ESHOT servisinden veri alınamadı.");
        problem.Status.Should().Be(502);
    }

    [Fact]
    public async Task InvokeAsync_ServiceUnavailableException_Returns503ProblemDetails()
    {
        // Arrange
        var middleware = new ExceptionMiddleware(
            next: (innerHttpContext) => throw new ServiceUnavailableException("ESHOT Unavailable"),
            logger: _loggerMock.Object
        );

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        
        var problem = JsonSerializer.Deserialize<ProblemDetails>(responseBody, _jsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Servis kullanılamıyor");
        problem.Detail.Should().Be("ESHOT servisine şu anda ulaşılamıyor.");
        problem.Status.Should().Be(503);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500ProblemDetailsWithoutStackTrace()
    {
        // Arrange
        var exceptionMessage = "This is a secret database error";
        var middleware = new ExceptionMiddleware(
            next: (innerHttpContext) => throw new Exception(exceptionMessage),
            logger: _loggerMock.Object
        );

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        
        var problem = JsonSerializer.Deserialize<ProblemDetails>(responseBody, _jsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Beklenmeyen hata");
        problem.Detail.Should().Be("Beklenmeyen bir uygulama hatası oluştu.");
        problem.Status.Should().Be(500);
        
        responseBody.Should().NotContain(exceptionMessage);
        responseBody.Should().NotContain("StackTrace");
    }
}

