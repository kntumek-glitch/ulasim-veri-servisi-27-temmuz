using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ulasim_veri_servisi.Exceptions;
using TransportDataService.Models.Exceptions;

namespace ulasim_veri_servisi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception)
    {
        var problem = new ProblemDetails();

        switch (exception)
        {
            case NotFoundException:
                problem.Title = "Kaynak bulunamadı";
                problem.Detail = "İstenen kaynak bulunamadı.";
                problem.Status = StatusCodes.Status404NotFound;
                break;

            case ActiveFeedNotFoundException ex:
                problem.Title = "Aktif GTFS Verisi Bulunamadı";
                problem.Detail = ex.Message;
                problem.Status = StatusCodes.Status404NotFound;
                break;

            case BadGatewayException:
                problem.Title = "Dış servis hatası";
                problem.Detail = "ESHOT servisinden veri alınamadı.";
                problem.Status = StatusCodes.Status502BadGateway;
                break;

            case ServiceUnavailableException:
                problem.Title = "Servis kullanılamıyor";
                problem.Detail = "ESHOT servisine şu anda ulaşılamıyor.";
                problem.Status = StatusCodes.Status503ServiceUnavailable;
                break;
                
            case SnapshotUnavailableException:
                problem.Title = "FEED_NOT_AVAILABLE";
                problem.Detail = "Routing graph is not loaded or is currently updating.";
                problem.Status = StatusCodes.Status503ServiceUnavailable;
                problem.Extensions["resolutionCode"] = "FEED_NOT_AVAILABLE";
                break;
                
            case ArgumentException ex:
                problem.Title = "Geçersiz İstek";
                problem.Detail = ex.Message;
                problem.Status = StatusCodes.Status400BadRequest;
                break;
                
            case OperationCanceledException:
                if (context.RequestAborted.IsCancellationRequested)
                {
                    problem.Title = "CLIENT_CANCELLED";
                    problem.Detail = "The client cancelled the request.";
                    problem.Status = 499; // Client Closed Request
                    problem.Extensions["resolutionCode"] = "CLIENT_CANCELLED";
                }
                else
                {
                    problem.Title = "SEARCH_TIMEOUT";
                    problem.Detail = "Search time limit exceeded.";
                    problem.Status = StatusCodes.Status408RequestTimeout;
                    problem.Extensions["resolutionCode"] = "SEARCH_TIMEOUT";
                }
                break;

            default:
                problem.Title = "INTERNAL_ERROR";
                problem.Detail = "An unexpected error occurred on the server.";
                problem.Status = StatusCodes.Status500InternalServerError;
                problem.Extensions["resolutionCode"] = "INTERNAL_ERROR";
                break;
        }

        problem.Type = $"https://httpstatuses.com/{problem.Status}";
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = problem.Status.Value;

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem));
    }
}

