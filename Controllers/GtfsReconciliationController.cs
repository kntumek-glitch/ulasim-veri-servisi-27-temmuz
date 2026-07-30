using Microsoft.AspNetCore.Mvc;
using ulasim_veri_servisi.Services;

namespace ulasim_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/reconciliation")]
public class GtfsReconciliationController : ControllerBase
{
    private readonly IGtfsStopReconciliationService _service;


    public GtfsReconciliationController(
        IGtfsStopReconciliationService service)
    {
        _service = service;
    }


    [HttpPost("gtfs-stops")]
    [ServiceFilter(typeof(ulasim_veri_servisi.Filters.AdminKeyAuthAttribute))]
    public async Task<IActionResult> Reconcile(
        CancellationToken cancellationToken)
    {
        var result = await _service.ReconcileAsync(
            cancellationToken);

        return Ok(result);
    }
}

