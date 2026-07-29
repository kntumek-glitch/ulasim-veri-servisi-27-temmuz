using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasım_veri_servisi.Services.Interfaces;
using Swashbuckle.AspNetCore.Annotations;
using Microsoft.AspNetCore.Http;

namespace ulasım_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/journey-plans")]
public class JourneyPlansController : ControllerBase
{
    private readonly IJourneyPlanningService _journeyPlanningService;

    public JourneyPlansController(IJourneyPlanningService journeyPlanningService)
    {
        _journeyPlanningService = journeyPlanningService;
    }

    /// <summary>
    /// Searches for a journey plan based on static GTFS data.
    /// </summary>
    /// <param name="request">Search parameters including origin, destination, and departure time.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of itineraries matching the criteria.</returns>
    [HttpPost("search")]
    [ProducesResponseType(typeof(JourneyPlanSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [SwaggerOperation(Summary = "Search Journey Plans", Description = "Calculates transit routes based on static GTFS scheduling. Results do not include real-time vehicle positions or traffic.")]
    public async Task<IActionResult> Search([FromBody] JourneyPlanSearchRequest request, CancellationToken cancellationToken)
    {
        // Validation is automatically handled by the framework via DataAnnotations and InvalidModelStateResponseFactory.
        
        var response = await _journeyPlanningService.SearchJourneyAsync(request, cancellationToken);
        return Ok(response);
    }
}
