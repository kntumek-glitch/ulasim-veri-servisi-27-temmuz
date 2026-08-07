using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TransportDataService;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services;

public class SnapshotWarmupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SnapshotWarmupService> _logger;

    public SnapshotWarmupService(IServiceScopeFactory scopeFactory, ILogger<SnapshotWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("SnapshotWarmupService starting...");
            
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var snapshotManager = scope.ServiceProvider.GetRequiredService<IRoutingSnapshotManager>();

            // Find the currently active GTFS Import Run
            var activeRun = await dbContext.GtfsImportRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.IsActive, stoppingToken);

            if (activeRun == null)
            {
                _logger.LogWarning("No active GTFS Import Run found in database. Snapshot warmup skipped.");
                return;
            }

            _logger.LogInformation("Found active GTFS Import Run ID: {RunId}. Initiating snapshot build...", activeRun.Id);
            await snapshotManager.BuildAndSwapSnapshotAsync(activeRun.Id, activeRun.FileHash ?? "UNKNOWN_HASH", stoppingToken);
            _logger.LogInformation("Snapshot warmup completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during snapshot warmup.");
        }
    }
}
