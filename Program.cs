

using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ulasim_veri_servisi.Middleware;
using Microsoft.Extensions.Caching.Memory;
using TransportDataService;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ulasim_veri_servisi.HealthChecks;
using ulasim_veri_servisi.Workers;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

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
    var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    
    options.AddPolicy("DynamicCors", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            }
        }
    });
});

var permitLimit = builder.Configuration.GetValue<int>("RateLimit:PermitLimit", 50);
var windowSeconds = builder.Configuration.GetValue<int>("RateLimit:WindowSeconds", 10);

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter(policyName: "JourneyPlanPolicy", config =>
    {
        config.PermitLimit = permitLimit;
        config.Window = TimeSpan.FromSeconds(windowSeconds);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 2; // Allow a tiny queue before rejecting
    });
    
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
        Title = "ulasim-veri-servisi",
        Version = "1.0",
        Description = "Transport data service API"
    });
    c.EnableAnnotations();
});

builder.Services.AddHttpClient<IExternalEshotService, ExternalEshotService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<ulasim_veri_servisi.Services.JourneyPlanCacheTokenSource>();
builder.Services.AddSingleton<ulasim_veri_servisi.Services.Interfaces.IRoutingSnapshotManager, ulasim_veri_servisi.Services.RoutingSnapshotManager>();
builder.Services.AddScoped<ulasim_veri_servisi.Services.Interfaces.IRaptorRoutingEngine, ulasim_veri_servisi.Services.RaptorRoutingEngine>();
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
builder.Services.AddScoped<ulasim_veri_servisi.Services.Interfaces.IJourneyPlanningService, ulasim_veri_servisi.Services.JourneyPlanningService>();
builder.Services.AddScoped<ulasim_veri_servisi.Services.JourneyPlanning.Spatial.ISpatialCalculatorService, ulasim_veri_servisi.Services.JourneyPlanning.Spatial.SpatialCalculatorService>();
builder.Services.AddScoped<ulasim_veri_servisi.Services.JourneyPlanning.DataAccess.IJourneyCacheService, ulasim_veri_servisi.Services.JourneyPlanning.DataAccess.JourneyCacheService>();
builder.Services.AddScoped<ulasim_veri_servisi.Services.JourneyPlanning.Algorithms.IJourneyRoutingEngine, ulasim_veri_servisi.Services.JourneyPlanning.Algorithms.JourneyRoutingEngine>();
builder.Services.AddScoped<ulasim_veri_servisi.Services.JourneyPlanning.Mapping.IJourneyResultMapper, ulasim_veri_servisi.Services.JourneyPlanning.Mapping.JourneyResultMapper>();

builder.Services.Configure<ulasim_veri_servisi.Models.WalkingRoutingCacheConfiguration>(builder.Configuration.GetSection("WalkingRoutingCache"));
builder.Services.Configure<ulasim_veri_servisi.Models.OsrmConfiguration>(builder.Configuration.GetSection("Osrm"));
builder.Services.AddHttpClient<IWalkingRouteProvider, OsrmWalkingRouteProvider>((sp, client) => {
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ulasim_veri_servisi.Models.OsrmConfiguration>>().Value;
    if (!string.IsNullOrWhiteSpace(config.BaseUrl))
    {
        client.BaseAddress = new Uri(config.BaseUrl);
    }
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
});
builder.Services.AddScoped<WalkingRoutingService>();
builder.Services.AddScoped<ulasim_veri_servisi.Filters.GtfsETagCacheFilterAttribute>();
builder.Services.AddScoped<ulasim_veri_servisi.Filters.AdminKeyAuthAttribute>();
builder.Services.AddScoped<IGtfsTransferCalculationService, GtfsTransferCalculationService>();

// builder.Services.AddHostedService<GtfsAutoUpdateWorker>();
builder.Services.AddHostedService<ulasim_veri_servisi.Services.SnapshotWarmupService>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })
    .AddCheck<RoutingEngineHealthCheck>("routing_engine", tags: new[] { "ready" })
    .AddCheck<EshotApiHealthCheck>("eshot_api", failureStatus: HealthStatus.Degraded, tags: new[] { "dependencies" })
    .AddCheck<GtfsDataHealthCheck>("gtfs_data", tags: new[] { "dependencies" });

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

app.UseResponseCompression();
app.UseCors("DynamicCors");
app.UseRateLimiter();

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
