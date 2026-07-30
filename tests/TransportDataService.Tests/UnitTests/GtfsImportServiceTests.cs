using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using System.Net;
using System.Security.Cryptography;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Tests.Helpers;
using ulasim_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

[Collection("PostgreSql collection")]
public class GtfsImportServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _context = null!;
    private Mock<HttpMessageHandler> _handlerMock = null!;
    private HttpClient _httpClient = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private GtfsImportService _service = null!;

    public GtfsImportServiceTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();
        await TruncateGtfsTablesAsync();

        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(_fixture.ConnectionString));
        services.AddLogging();
        services.AddMemoryCache();
        
        var sp = services.BuildServiceProvider();
        _scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GtfsImportService>>();
        var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        
        _service = new GtfsImportService(_scopeFactory, _httpClient, logger, cache, configuration);
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    private async Task TruncateGtfsTablesAsync()
    {
        await _context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "GtfsStopTimes", "GtfsShapePoints", "GtfsCalendars",
                "GtfsCalendarDates", "GtfsTrips", "GtfsStops", "GtfsRoutes",
                "GtfsAgencies", "GtfsImportRuns" RESTART IDENTITY CASCADE
            """);
    }

    private void SetupZipResponse(byte[] zipBytes)
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent(zipBytes)
            });
    }

    private static async Task<TableCounts> GetTableCounts(AppDbContext ctx) => new(
        await ctx.GtfsAgencies.CountAsync(),
        await ctx.GtfsRoutes.CountAsync(),
        await ctx.GtfsStops.CountAsync(),
        await ctx.GtfsTrips.CountAsync(),
        await ctx.GtfsStopTimes.CountAsync(),
        await ctx.GtfsCalendars.CountAsync(),
        await ctx.GtfsShapePoints.CountAsync());

    private record TableCounts(int Agencies, int Routes, int Stops, int Trips, int StopTimes, int Calendars, int ShapePoints);

    [Fact]
    public async Task ImportAsync_WhenHashExists_SkipsAndLeavesTablesUnchanged()
    {
        var zipBytes = MinimalGtfsZipBuilder.Build();
        var fileHash = Convert.ToHexString(SHA256.HashData(zipBytes));

        var run = new GtfsImportRun { FileHash = fileHash, Status = "Completed", IsActive = true };
        _context.GtfsImportRuns.Add(run);
        _context.GtfsStops.Add(new GtfsStop { GtfsImportRun = run, StopId = "existing" });
        await _context.SaveChangesAsync();
        var before = await GetTableCounts(_context);

        SetupZipResponse(zipBytes);
        var result = await _service.ImportAsync(CancellationToken.None);

        result.Status.Should().Be("Skipped");
        result.FileHash.Should().Be(fileHash);
        result.FinishedAt.Should().NotBeNull();
        result.FinishedAt.Should().BeOnOrAfter(result.StartedAt);
        (await GetTableCounts(_context)).Should().Be(before);
    }

    [Fact]
    public async Task ImportAsync_ValidZip_CompletesAndPersistsData()
    {
        SetupZipResponse(MinimalGtfsZipBuilder.Build());
        var result = await _service.ImportAsync(CancellationToken.None);

        result.Status.Should().Be("Completed");
        result.FinishedAt.Should().NotBeNull();
        result.FinishedAt.Should().BeOnOrAfter(result.StartedAt);
        result.AgencyCount.Should().Be(1);
        result.RouteCount.Should().Be(1);
        result.StopCount.Should().Be(11);
        result.TripCount.Should().Be(11);
        result.StopTimeCount.Should().Be(101);

        (await _context.GtfsAgencies.CountAsync()).Should().Be(1);
        (await _context.GtfsRoutes.CountAsync()).Should().Be(1);
        (await _context.GtfsStops.CountAsync()).Should().Be(11);
        (await _context.GtfsTrips.CountAsync()).Should().Be(11);
        (await _context.GtfsStopTimes.CountAsync()).Should().Be(101);
    }

    [Fact]
    public async Task ImportAsync_ValidZip_ParsesStopTimesViaDomainParser()
    {
        SetupZipResponse(MinimalGtfsZipBuilder.Build());
        await _service.ImportAsync(CancellationToken.None);

        var stopTime = await _context.GtfsStopTimes.FirstAsync();
        stopTime.ArrivalSeconds.Should().Be(91845); // 25:30:45
        stopTime.DepartureSeconds.Should().Be(91860); // 25:31:00
    }

    [Fact]
    public async Task ImportAsync_InvalidZip_SetsFailed()
    {
        SetupZipResponse(new byte[] { 0x00, 0x01, 0x02 }); // Geçersiz ZIP

        var act = async () => await _service.ImportAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();

        var dbRun = await _context.GtfsImportRuns.SingleAsync();
        dbRun.Status.Should().Be("Failed");
        dbRun.FinishedAt.Should().NotBeNull();

        (await _context.GtfsStops.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_HttpError_SetsFailed()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError });

        var act = async () => await _service.ImportAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();

        (await _context.GtfsImportRuns.SingleAsync()).Status.Should().Be("Failed");
    }

    [Fact]
    public async Task ImportAsync_ConcurrentCalls_DoNotCorruptData()
    {
        SetupZipResponse(MinimalGtfsZipBuilder.Build());
        var tasks = Enumerable.Range(0, 2).Select(async _ => 
        {
            try
            {
                return await _service.ImportAsync(CancellationToken.None);
            }
            catch (ConcurrentImportException)
            {
                return null;
            }
        });
        var results = await Task.WhenAll(tasks);

        var successfulRuns = results.Count(r => r != null && r.Status == "Completed");
        var failedDueToConcurrency = results.Count(r => r == null);

        successfulRuns.Should().Be(1, "Sadece bir import işlemi başarılı olmalıdır.");
        failedDueToConcurrency.Should().Be(1, "Diğer işlem ConcurrentImportException fırlatmalıdır.");

        (await _context.GtfsImportRuns.CountAsync(r => r.Status == "Completed")).Should().Be(1);
        (await _context.GtfsStops.CountAsync()).Should().Be(11);
    }

    [Fact]
    public async Task ImportAsync_StuckRuns_AreCleanedUpBeforeNewImport()
    {
        // Arrange
        var stuckRun = new GtfsImportRun
        {
            SourceUrl = "http://test",
            StartedAt = DateTime.UtcNow.AddHours(-1),
            Status = "Running"
        };
        _context.GtfsImportRuns.Add(stuckRun);
        await _context.SaveChangesAsync();

        SetupZipResponse(MinimalGtfsZipBuilder.Build());

        // Act
        var result = await _service.ImportAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be("Completed");

        var dbStuckRun = await _context.GtfsImportRuns.AsNoTracking().SingleAsync(r => r.Id == stuckRun.Id);
        dbStuckRun.Status.Should().Be("Failed");
        dbStuckRun.FinishedAt.Should().NotBeNull();
        dbStuckRun.ErrorMessage.Should().Contain("Automatically marked as Failed");
    }
}

