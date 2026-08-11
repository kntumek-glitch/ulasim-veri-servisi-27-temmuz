using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportDataService;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.HealthChecks;

public class RoutingEngineHealthCheck : IHealthCheck
{
    private readonly AppDbContext _context;
    private readonly IRoutingSnapshotManager _snapshotManager;

    public RoutingEngineHealthCheck(AppDbContext context, IRoutingSnapshotManager snapshotManager)
    {
        _context = context;
        _snapshotManager = snapshotManager;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Snapshot Presence
            var snapshot = _snapshotManager.GetActiveSnapshot();
            if (snapshot == null)
            {
                return HealthCheckResult.Unhealthy("Routing Snapshot is missing from memory.");
            }

            // 2. Database Connectivity & Feed Presence
            var activeRun = await _context.GtfsImportRuns
                .Where(r => r.IsActive && r.Status == "Completed")
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeRun == null)
            {
                return HealthCheckResult.Unhealthy("No active GTFS feed found in the database.");
            }

            // 3. Integrity Sync (Hash Matching)
            if (snapshot.FeedHash != activeRun.FileHash)
            {
                return HealthCheckResult.Unhealthy(
                    $"Snapshot integrity mismatch! Memory Hash: {snapshot.FeedHash}, DB Hash: {activeRun.FileHash}. System is not synchronized.");
            }

            // All criteria met. Engine is fully operational and synchronized.
            var data = new Dictionary<string, object>
            {
                { "active_import_id", snapshot.ActiveImportId },
                { "feed_hash", snapshot.FeedHash },
                { "is_synchronized", true }
            };

            return HealthCheckResult.Healthy("V2 Routing Engine is synchronized and ready for production traffic.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to verify Routing Snapshot integrity due to a database connection error.", ex);
        }
    }
}
