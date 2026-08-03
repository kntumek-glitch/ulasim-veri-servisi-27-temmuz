using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportDataService;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/admin/transfers")]
public class AdminController : ControllerBase
{
    private readonly IGtfsTransferCalculationService _transferCalculationService;
    private readonly AppDbContext _context;

    public AdminController(IGtfsTransferCalculationService transferCalculationService, AppDbContext context)
    {
        _transferCalculationService = transferCalculationService;
        _context = context;
    }

    [HttpPost("rebuild")]
    public async Task<IActionResult> RebuildTransfers(CancellationToken cancellationToken)
    {
        var activeRun = await _context.GtfsImportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IsActive && r.Status == "Completed", cancellationToken);

        if (activeRun == null)
        {
            return NotFound(new { Message = "No active GTFS import run found." });
        }

        try
        {
            int insertedCount = await _transferCalculationService.RebuildTransfersAsync(activeRun.Id, cancellationToken);
            return Ok(new { Message = "Transfers rebuilt successfully.", TotalTransfers = insertedCount, RunId = activeRun.Id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = "An error occurred while rebuilding transfers.", Error = ex.Message });
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetTransfersStatus(CancellationToken cancellationToken)
    {
        var activeRun = await _context.GtfsImportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IsActive && r.Status == "Completed", cancellationToken);

        if (activeRun == null)
        {
            return NotFound(new { Message = "No active GTFS import run found." });
        }

        var totalTransfers = await _context.GtfsTransfers
            .CountAsync(t => t.GtfsImportRunId == activeRun.Id, cancellationToken);

        return Ok(new
        {
            ActiveRunId = activeRun.Id,
            TotalTransfers = totalTransfers,
            LastUpdatedAt = DateTime.UtcNow
        });
    }
}
