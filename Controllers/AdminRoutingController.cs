using Microsoft.AspNetCore.Mvc;
using ulasim_veri_servisi.Filters;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Controllers;

[ApiController]
[Route("api/v2/admin/routing")]
[ServiceFilter(typeof(AdminKeyAuthAttribute))]
public class AdminRoutingController : ControllerBase
{
    private readonly IRoutingSnapshotManager _snapshotManager;

    public AdminRoutingController(IRoutingSnapshotManager snapshotManager)
    {
        _snapshotManager = snapshotManager;
    }

    [HttpGet("snapshot")]
    public IActionResult GetActiveSnapshotStatus()
    {
        var snapshot = _snapshotManager.GetActiveSnapshot();

        if (snapshot == null)
        {
            return Ok(new
            {
                is_ready = false
            });
        }

        return Ok(new
        {
            is_ready = true,
            active_import_id = snapshot.ActiveImportId,
            feed_hash = snapshot.FeedHash,
            created_at = snapshot.CreatedAt,
            stop_count = snapshot.Stops.Count,
            pattern_count = snapshot.PatternMetadata.Count,
            trip_count = snapshot.TripTimetables.Count,
            transfer_count = snapshot.StopTransfers.Count,
            build_duration_ms = snapshot.BuildDurationMs,
            estimated_memory_bytes = snapshot.EstimatedMemoryBytes
        });
    }
}
