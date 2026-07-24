using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportDataService;
using ulasım_veri_servisi.Models.Gtfs;
using ulasım_veri_servisi.Services;

namespace ulasım_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/import")]
public class GtfsImportController : ControllerBase
{
    private readonly IGtfsImportService _gtfsImportService;
    private readonly AppDbContext _context;

    public GtfsImportController(IGtfsImportService gtfsImportService, AppDbContext context)
    {
        _gtfsImportService = gtfsImportService;
        _context = context;
    }

    [HttpPost("gtfs")]
    [ServiceFilter(typeof(ulasım_veri_servisi.Filters.AdminKeyAuthAttribute))]
    public async Task<IActionResult> ImportGtfs(CancellationToken cancellationToken)
    {
        var result = await _gtfsImportService.ImportAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("gtfs/runs")]
    public async Task<ActionResult<PaginatedResponse<GtfsImportRunResponse>>> GetRuns(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.GtfsImportRuns.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            query = query.Where(run => run.Status.ToUpper() == normalizedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var runs = await query
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = runs.Select(GtfsImportRunResponse.From).ToList();

        return Ok(new PaginatedResponse<GtfsImportRunResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("gtfs/runs/{id:int}")]
    public async Task<ActionResult<GtfsImportRunResponse>> GetRun(int id, CancellationToken cancellationToken)
    {
        var run = await _context.GtfsImportRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Id == id, cancellationToken);
        if (run is null)
            return Problem(detail: "İstenen import kaydı bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        return Ok(GtfsImportRunResponse.From(run));
    }
}
