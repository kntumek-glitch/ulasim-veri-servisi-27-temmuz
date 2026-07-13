

using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Caching.Memory;
using TransportDataService;
using ulasım_veri_servisi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.Services.AddControllers();


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));
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
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ApproachingBusService>();
builder.Services.AddScoped<RouteVehiclesService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ulasım-veri-servisi v1");
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();