using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using TransportDataService;
using TransportDataService.Models.Gtfs.Transfers;
using ulasim_veri_servisi.Filters;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/gtfs/transfers")]
public class GtfsTransfersController : ControllerBase
{
    private static readonly SemaphoreSlim _rebuildLock = new SemaphoreSlim(1, 1);
    
    private readonly AppDbContext _context;
    private readonly IGtfsTransferCalculationService _calculationService;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GtfsTransfersController> _logger;

    public GtfsTransfersController(
        AppDbContext context,
        IGtfsTransferCalculationService calculationService,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<GtfsTransfersController> logger)
    {
        _context = context;
        _calculationService = calculationService;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    [HttpPost("/api/v1/admin/gtfs/transfers/rebuild")]
    [ServiceFilter(typeof(AdminKeyAuthAttribute))]
    public async Task<IActionResult> RebuildTransfers(CancellationToken cancellationToken)
    {
        var activeRun = await _context.GtfsImportRuns.FirstOrDefaultAsync(r => r.IsActive, cancellationToken);
        if (activeRun == null)
        {
            return BadRequest(new ProblemDetails { Title = "Hata", Detail = "Sistemde aktif bir GTFS Import bulunamadı." });
        }

        // Concurrency kilidi (Eşzamanlı başlatmayı engelle)
        if (!await _rebuildLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Rebuild işlemi zaten devam ediyor.");
            return Conflict(new ProblemDetails { Title = "Çakışma", Detail = "Şu anda devam eden bir rebuild işlemi var. Lütfen daha sonra tekrar deneyin." });
        }

        try
        {
            var sw = Stopwatch.StartNew();
            
            // Transactional Rebuild
            int count = await _calculationService.RebuildTransfersAsync(activeRun.Id, cancellationToken);
            
            sw.Stop();

            // Clear journey planning cache since transfers are updated
            if (_cache is MemoryCache memoryCache)
            {
                memoryCache.Clear();
            }

            int maxWalkMeters = _configuration.GetValue<int>("JourneyPlan:MaxTransferWalkMeters", 1500);

            var response = new TransferNetworkRebuildResponse
            {
                TransferCount = count,
                ExecutionTimeMs = sw.ElapsedMilliseconds,
                MaxWalkingDistanceMeters = maxWalkMeters
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rebuild sırasında beklenmeyen bir hata oluştu.");
            return StatusCode(500, new ProblemDetails { Title = "Sunucu Hatası", Detail = "Rebuild işlemi başarısız oldu ve geri alındı." });
        }
        finally
        {
            _rebuildLock.Release();
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var activeRun = await _context.GtfsImportRuns
            .Include(r => r.Phases)
            .FirstOrDefaultAsync(r => r.IsActive, cancellationToken);

        if (activeRun == null)
        {
            return NotFound(new ProblemDetails { Title = "Bulunamadı", Detail = "Sistemde aktif bir GTFS Import bulunamadı." });
        }

        var transferPhase = activeRun.Phases.FirstOrDefault(p => p.PhaseName == "CalculatingTransfers" || p.PhaseName == "RebuildTransfers");
        
        int count = await _context.GtfsTransfers.CountAsync(t => t.GtfsImportRunId == activeRun.Id, cancellationToken);
        int maxWalkMeters = _configuration.GetValue<int>("JourneyPlan:MaxTransferWalkMeters", 1500);

        bool isReady = count > 0 && transferPhase?.ErrorMessage == null;
        long? processingTime = null;

        if (transferPhase != null && transferPhase.FinishedAt.HasValue)
        {
            processingTime = (long)(transferPhase.FinishedAt.Value - transferPhase.StartedAt).TotalMilliseconds;
        }
        else if (count > 0)
        {
            // Eğer phase yoksa fakat veritabanında count varsa (örn. manual seeding) processingTime'a tahmin / boş atanabilir.
            processingTime = 0;
        }

        var response = new TransferNetworkStatusResponse
        {
            ActiveImportId = activeRun.Id,
            CalculationDate = transferPhase?.FinishedAt ?? (count > 0 ? DateTime.UtcNow : null),
            TransferCount = count,
            MaxWalkingDistanceMeters = maxWalkMeters,
            CalculationMethod = "Haversine",
            IsReady = isReady,
            ProcessingTimeMs = processingTime,
            DataVersion = activeRun.FeedVersion ?? activeRun.FileHash ?? activeRun.Id.ToString()
        };

        return Ok(response);
    }
}
