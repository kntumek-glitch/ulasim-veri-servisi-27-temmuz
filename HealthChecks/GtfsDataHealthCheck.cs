using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportDataService;

namespace ulasım_veri_servisi.HealthChecks
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
                    .OrderByDescending(r => r.FinishedAt ?? r.DownloadedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                var data = new Dictionary<string, object>
                {
                    { "is_gtfs_data_loaded", run != null }
                };

                if (run != null)
                {
                    var date = run.FinishedAt ?? run.DownloadedAt;
                    data.Add("last_successful_import", date.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    return HealthCheckResult.Healthy("GTFS data is loaded.", data);
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
