using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ulasım_veri_servisi.Exceptions;

namespace ulasım_veri_servisi.Middleware;

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
            _logger.LogError(ex, ex.Message);

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

            default:
                problem.Title = "Beklenmeyen hata";
                problem.Detail = "Beklenmeyen bir uygulama hatası oluştu.";
                problem.Status = StatusCodes.Status500InternalServerError;
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
