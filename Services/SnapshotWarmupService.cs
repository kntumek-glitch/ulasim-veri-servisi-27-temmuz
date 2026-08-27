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
    private const int MaxRetryAttempts = 5;
    private const int InitialRetryDelaySeconds = 5;
    private const int NoDataRetryDelaySeconds = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SnapshotWarmupService> _logger;

    public SnapshotWarmupService(IServiceScopeFactory scopeFactory, ILogger<SnapshotWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SnapshotWarmupService starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var snapshotManager = scope.ServiceProvider.GetRequiredService<IRoutingSnapshotManager>();

                var activeRun = await dbContext.GtfsImportRuns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IsActive, stoppingToken);

                if (activeRun == null)
                {
                    _logger.LogInformation("No active GTFS Import Run found in database. Waiting for data...");
                    await Task.Delay(TimeSpan.FromSeconds(NoDataRetryDelaySeconds), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Found active GTFS Import Run ID: {RunId}. Initiating snapshot build...", activeRun.Id);
                var candidate = await snapshotManager.BuildCandidateSnapshotAsync(activeRun.Id, activeRun.FileHash ?? "UNKNOWN_HASH", stoppingToken);
                snapshotManager.PromoteSnapshot(candidate);
                _logger.LogInformation("Snapshot warmup completed successfully.");
                return; // Success – exit the loop
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("SnapshotWarmupService cancelled.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Snapshot warmup failed. Retrying in 30s...");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
