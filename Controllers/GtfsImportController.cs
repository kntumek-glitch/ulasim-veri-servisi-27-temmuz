using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportDataService;
using ulasım_veri_servisi.Models.Gtfs;
using ulasım_veri_servisi.Services;
using System.Net.Http;

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
        try
        {
            var result = await _gtfsImportService.ImportAsync(cancellationToken);
            
            int? previousSuccessful = await _context.GtfsImportRuns
                .Where(x => x.Status == "Completed" && x.Id != result.Id)
                .OrderByDescending(x => x.Id)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var dto = GtfsImportResponseDto.FromRun(result, previousSuccessful);
            
            if (result.Status == "Skipped")
            {
                return Ok(dto);
            }
            
            return StatusCode(StatusCodes.Status201Created, dto);
        }
        catch (ConcurrentImportException ex)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Conflict", 
                Detail = ex.Message, 
                Status = StatusCodes.Status409Conflict 
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails 
            { 
                Title = "Bad Gateway", 
                Detail = "Dış kaynağa erişim sağlanamadı.", 
                Status = StatusCodes.Status502BadGateway 
            });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new ProblemDetails 
            { 
                Title = "Gateway Timeout", 
                Detail = "Dış kaynak zaman aşımına uğradı.", 
                Status = StatusCodes.Status504GatewayTimeout 
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails 
            { 
                Title = "Internal Server Error", 
                Detail = "Beklenmeyen bir hata oluştu veya dosya ayrıştırılamadı.", 
                Status = StatusCodes.Status500InternalServerError 
            });
        }
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
