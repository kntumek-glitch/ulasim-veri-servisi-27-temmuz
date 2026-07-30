using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportDataService;

namespace ulasim_veri_servisi.HealthChecks
{
    public class GtfsDataHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _context;

        public GtfsDataHealthCheck(AppDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var run = await _context.GtfsImportRuns
                    .Where(r => r.IsActive && r.Status == "Completed")
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                var data = new Dictionary<string, object>
                {
                    { "is_gtfs_data_loaded", run != null },
                    { "next_auto_import_time", ulasim_veri_servisi.Workers.GtfsAutoUpdateWorker.NextRunTime?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "Unknown" }
                };

                if (run != null)
                {
                    var finishedAt = run.FinishedAt ?? run.StartedAt;
                    var ageHours = Math.Round((DateTime.UtcNow - finishedAt).TotalHours, 2);
                    var durationSeconds = run.FinishedAt.HasValue ? Math.Round((run.FinishedAt.Value - run.StartedAt).TotalSeconds, 2) : 0;

                    data.Add("active_feed_id", run.Id);
                    data.Add("active_feed_hash", run.FileHash ?? "N/A");
                    data.Add("active_feed_age_hours", ageHours);
                    data.Add("last_import_status", run.Status);
                    data.Add("last_successful_import_time", finishedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    data.Add("last_import_duration_seconds", durationSeconds);
                    
                    data.Add("metrics", new
                    {
                        agencyCount = run.AgencyCount,
                        routeCount = run.RouteCount,
                        stopCount = run.StopCount,
                        tripCount = run.TripCount,
                        stopTimeCount = run.StopTimeCount,
                        shapePointCount = run.ShapePointCount
                    });
                    
                    data.Add("validation_issues", run.FailedRecordCount);

                    var status = ageHours > 48 ? HealthStatus.Degraded : HealthStatus.Healthy;
                    var message = ageHours > 48 ? $"GTFS data is loaded but stale ({ageHours} hours old)." : "GTFS data is loaded and up to date.";

                    return new HealthCheckResult(status, message, null, data);
                }

                return HealthCheckResult.Unhealthy("No successful GTFS import found.", null, data);
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Failed to query GTFS data status.", ex);
            }
        }
    }
}

