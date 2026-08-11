using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Tests.Helpers;
using ulasim_veri_servisi.Models.Routing;
using ulasim_veri_servisi.Services;
using ulasim_veri_servisi.Services.Interfaces;
using Xunit;
using System;
using System.IO;

namespace TransportDataService.Tests.IntegrationTests;

[Collection("PostgreSql collection")]
public class AtomicPromotionIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _context = null!;
    private Mock<HttpMessageHandler> _handlerMock = null!;
    private HttpClient _httpClient = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private Mock<IRoutingSnapshotManager> _mockSnapshotManager = null!;
    private GtfsImportService _service = null!;

    public AtomicPromotionIntegrationTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();
        
        // Truncate runs
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"GtfsImportRuns\" CASCADE;");

        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(_fixture.ConnectionString));
        services.AddLogging();
        services.AddMemoryCache();
        var mockTransferService = new Mock<IGtfsTransferCalculationService>();
        services.AddScoped(sp => mockTransferService.Object);
        
        var sp = services.BuildServiceProvider();
        _scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GtfsImportService>>();
        var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        
        _mockSnapshotManager = new Mock<IRoutingSnapshotManager>();
        
        _service = new GtfsImportService(_scopeFactory, _httpClient, logger, cache, configuration, _mockSnapshotManager.Object);
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ImportAsync_WhenSnapshotBuildFails_ShouldNotActivateDatabaseFeed()
    {
        // Arrange
        // Create an active, existing run to simulate current state.
        var initialRun = new GtfsImportRun
        {
            Id = 1,
            IsActive = true,
            Status = "Completed",
            StartedAt = DateTime.UtcNow.AddDays(-1),
            FinishedAt = DateTime.UtcNow.AddDays(-1),
            FileHash = "OLD_HASH"
        };
        _context.GtfsImportRuns.Add(initialRun);
        await _context.SaveChangesAsync();

        // Setup HttpClient to return a fake minimal zip
        var fakeZipPath = Path.Combine(AppContext.BaseDirectory, "Helpers", "test_gtfs.zip");
        byte[] zipBytes = File.Exists(fakeZipPath) ? await File.ReadAllBytesAsync(fakeZipPath) : new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // minimal empty zip
        
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent(zipBytes)
            });

        // Simüle edilen hata: Snapshot inşası OutOfMemory veya başka bir nedenle çökerse!
        _mockSnapshotManager
            .Setup(x => x.BuildCandidateSnapshotAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated OutOfMemory exception during Candidate Snapshot Build!"));

        // Act
        Func<Task> act = async () => await _service.ImportAsync(CancellationToken.None);

        // Assert
        // The service should catch the exception internally or throw it up. Either way, DB state should be verified.
        // GtfsImportService currently doesn't throw up but logs and sets run to Failed, actually it might rethrow or swallow. Wait, let's just run it.
        try
        {
            await act.Invoke();
        }
        catch
        {
            // Ignore for the purpose of checking DB state
        }

        // Verify DB State
        // 1. Initial run should still be IsActive = true
        var initialDbRun = await _context.GtfsImportRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
        initialDbRun.Should().NotBeNull();
        initialDbRun!.IsActive.Should().BeTrue("The previous GTFS feed must remain active if the new one fails snapshot building.");

        // 2. The new run should exist but be IsActive = false
        var newRun = await _context.GtfsImportRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id != 1);
        newRun.Should().NotBeNull();
        newRun!.IsActive.Should().BeFalse("The newly downloaded GTFS feed must NOT become active because its snapshot failed.");
        
        // 3. Ensure PromoteSnapshot was NEVER called!
        _mockSnapshotManager.Verify(x => x.PromoteSnapshot(It.IsAny<RoutingSnapshot>()), Times.Never);
    }
}
