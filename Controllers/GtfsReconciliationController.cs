using Microsoft.AspNetCore.Mvc;
using ulasım_veri_servisi.Services;

namespace ulasım_veri_servisi.Controllers;

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
    [ServiceFilter(typeof(ulasım_veri_servisi.Filters.AdminKeyAuthAttribute))]
    public async Task<IActionResult> Reconcile(
        CancellationToken cancellationToken)
    {
        await _service.ReconcileAsync(
            cancellationToken);

        return Ok(new
        {
            status = "Completed",
            message = "GTFS stop reconciliation report created."
        });
    }
}
