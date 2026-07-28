

using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ulasım_veri_servisi.Middleware;
using Microsoft.Extensions.Caching.Memory;
using TransportDataService;
using ulasım_veri_servisi.Services;
using ulasım_veri_servisi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ulasım_veri_servisi.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
Console.WriteLine("ContentRoot: " + builder.Environment.ContentRootPath);

// Add services to the container.


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddRouting(options => options.LowercaseUrls = true);

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});


builder.Services
    .AddHttpClient<IGtfsImportService, GtfsImportService>();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Title = "Geçersiz istek",
            Detail = "Gönderilen parametreler doğrulanamadı.",
            Status = StatusCodes.Status400BadRequest,
            Type = "https://httpstatuses.com/400"
        };

        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(problem);
    };
});




builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.CommandTimeout(600)));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ulasım-veri-servisi",
        Version = "1.0",
        Description = "Transport data service API"
    });
    c.EnableAnnotations();
});

builder.Services.AddHttpClient<IExternalEshotService, ExternalEshotService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddMemoryCache(options => { options.SizeLimit = 10000; });
builder.Services.AddScoped<ApproachingBusService>();
builder.Services.AddScoped<RouteVehiclesService>();
builder.Services.AddScoped<
    IGtfsStopReconciliationService,
    GtfsStopReconciliationService>();
builder.Services.AddHttpClient<CsvImportService>();
builder.Services.AddScoped<ITripStopsRepository, TripStopsRepository>();
builder.Services.AddScoped<ITripStopsService, TripStopsService>();
builder.Services.AddScoped<IRouteDeparturesService, RouteDeparturesService>();
builder.Services.AddScoped<ulasım_veri_servisi.Filters.GtfsETagCacheFilterAttribute>();
builder.Services.AddScoped<ulasım_veri_servisi.Filters.AdminKeyAuthAttribute>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })
    .AddCheck<EshotApiHealthCheck>("eshot_api", failureStatus: HealthStatus.Degraded, tags: new[] { "dependencies" })
    .AddCheck<GtfsDataHealthCheck>("gtfs_data", tags: new[] { "dependencies" });

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseResponseCompression();
app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Sadece uygulamanın ayakta olup olmadığını kontrol eder
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status == HealthStatus.Unhealthy ? "DOWN" : "UP",
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => e.Value.Status == HealthStatus.Unhealthy ? "DOWN" : "UP"
            )
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/dependencies", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("dependencies"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var dependencies = new Dictionary<string, object>();
        foreach (var entry in report.Entries)
        {
            var dict = new Dictionary<string, object>
            {
                { "status", entry.Value.Status == HealthStatus.Unhealthy ? "DOWN" : "UP" }
            };

            if (entry.Value.Data != null)
            {
                foreach (var dataItem in entry.Value.Data)
                {
                    dict[dataItem.Key] = dataItem.Value;
                }
            }
            dependencies[entry.Key] = dict;
        }

        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status == HealthStatus.Unhealthy ? "DOWN" : "UP",
            dependencies = dependencies
        });
        
        await context.Response.WriteAsync(result);
    }
});


app.Run();

public partial class Program { }