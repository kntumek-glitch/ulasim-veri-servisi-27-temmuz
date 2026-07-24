using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Moq.Protected;
using System.Net;
using System.Security.Cryptography;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Tests.Helpers;
using ulasım_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

[Collection("PostgreSql collection")]
public class GtfsImportServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _context = null!;
    private Mock<HttpMessageHandler> _handlerMock = null!;
    private HttpClient _httpClient = null!;
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
        _service = new GtfsImportService(_context, _httpClient);
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

        _context.GtfsImportRuns.Add(new GtfsImportRun { FileHash = fileHash, Status = "Completed" });
        _context.GtfsStops.Add(new GtfsStop { StopId = "existing" });
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
        result.StopCount.Should().Be(1);
        result.TripCount.Should().Be(1);
        result.StopTimeCount.Should().Be(1);

        (await _context.GtfsAgencies.CountAsync()).Should().Be(1);
        (await _context.GtfsRoutes.CountAsync()).Should().Be(1);
        (await _context.GtfsStops.CountAsync()).Should().Be(1);
        (await _context.GtfsTrips.CountAsync()).Should().Be(1);
        (await _context.GtfsStopTimes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_ValidZip_ParsesStopTimesViaDomainParser()
    {
        SetupZipResponse(MinimalGtfsZipBuilder.Build());
        await _service.ImportAsync(CancellationToken.None);

        var stopTime = await _context.GtfsStopTimes.SingleAsync();
        stopTime.ArrivalSeconds.Should().Be(91845); // 25:30:45
        stopTime.DepartureSeconds.Should().Be(91860); // 25:31:00
    }

    [Fact]
    public async Task ImportAsync_InvalidZip_SetsFailedAndRollsBack()
    {
        SetupZipResponse(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 });
        var result = await _service.ImportAsync(CancellationToken.None);

        result.Status.Should().Be("Failed");
        result.FinishedAt.Should().NotBeNull();
        result.ErrorMessage.Should().NotBeNullOrEmpty();

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

        var result = await _service.ImportAsync(CancellationToken.None);

        result.Status.Should().Be("Failed");
        result.FinishedAt.Should().NotBeNull();
        (await _context.GtfsImportRuns.SingleAsync()).Status.Should().Be("Failed");
    }

    [Fact]
    public async Task ImportAsync_ConcurrentCalls_DoNotCorruptData()
    {
        SetupZipResponse(MinimalGtfsZipBuilder.Build());
        var tasks = Enumerable.Range(0, 3).Select(_ => _service.ImportAsync(CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        results.Should().Contain(r => r.Status is "Completed" or "Skipped" or "Failed");
        (await _context.GtfsImportRuns.CountAsync(r => r.Status == "Completed")).Should().BeLessThanOrEqualTo(1);
        (await _context.GtfsStops.CountAsync()).Should().BeLessThanOrEqualTo(1);
    }
}
