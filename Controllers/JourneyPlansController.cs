using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using TransportDataService.Models.Gtfs.JourneyPlan;
using TransportDataService.Models.Exceptions;
using ulasim_veri_servisi.Services.Interfaces;
using Swashbuckle.AspNetCore.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Collections.Generic;

namespace ulasim_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/journey-plans")]
public class JourneyPlansController : ControllerBase
{
    private readonly IJourneyPlanningService _journeyPlanningService;
    private readonly IConfiguration _configuration;

    public JourneyPlansController(IJourneyPlanningService journeyPlanningService, IConfiguration configuration)
    {
        _journeyPlanningService = journeyPlanningService;
        _configuration = configuration;
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
        if (request.DepartureDateTime == null || request.DepartureDateTime.Value.Year < 2000)
        {
            var problemDetails = new ValidationProblemDetails(new Dictionary<string, string[]> 
            { 
                { "DepartureDateTime", new[] { "Geçerli bir tarih ve saat (departureDateTime) belirtilmelidir." } } 
            });
            return BadRequest(problemDetails);
        }
        
        int timeoutSeconds = _configuration.GetValue<int>("JourneyPlan:MaxSearchTimeSeconds", 15);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var response = await _journeyPlanningService.SearchJourneyAsync(request, cts.Token);
        return Ok(response);
    }

    /// <summary>
    /// Searches for a journey plan based on static GTFS data with advanced routing modes.
    /// </summary>
    /// <param name="request">Search parameters including origin, destination, time, and routing mode.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of itineraries matching the criteria.</returns>
    [HttpPost("~/api/v2/journey-plans/search")]
    [ProducesResponseType(typeof(JourneyPlanSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [SwaggerOperation(Summary = "Search Journey Plans (V2)", Description = "Calculates transit routes using advanced DEPART_AT mode via the highly optimized in-memory RAPTOR engine.")]
    public async Task<IActionResult> SearchV2([FromBody] JourneyPlanV2SearchRequest request, [FromServices] IRaptorRoutingEngine raptorEngine, CancellationToken cancellationToken)
    {
        if (request.DateTime == null || request.DateTime.Value.Year < 2000)
        {
            var problemDetails = new ValidationProblemDetails(new Dictionary<string, string[]> 
            { 
                { "DateTime", new[] { "Geçerli bir tarih ve saat (dateTime) belirtilmelidir." } } 
            });
            return BadRequest(problemDetails);
        }
        
        int timeoutSeconds = _configuration.GetValue<int>("JourneyPlan:MaxSearchTimeSeconds", 15);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try 
        {
            var response = await raptorEngine.SearchJourneyV2Async(request, cts.Token);
            
            if (response.ReasonCode == "FEED_STALE" || response.ReasonCode == "NO_ACTIVE_SERVICE")
            {
                return BadRequest(response);
            }

            if (!response.Itineraries.Any())
            {
                response.ReasonCode = JourneyPlanResolutionCode.NO_ROUTE_FOUND.ToString();
            }
            else 
            {
                response.ReasonCode = JourneyPlanResolutionCode.SUCCESS.ToString();
            }
            
            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            var code = cancellationToken.IsCancellationRequested 
                ? JourneyPlanResolutionCode.CLIENT_CANCELLED 
                : JourneyPlanResolutionCode.SEARCH_TIMEOUT;
                
            return GenerateErrorResponse(code, StatusCodes.Status408RequestTimeout, "Search time limit exceeded.");
        }
        catch (SnapshotUnavailableException)
        {
            return GenerateErrorResponse(JourneyPlanResolutionCode.FEED_NOT_AVAILABLE, StatusCodes.Status503ServiceUnavailable, "Routing graph is not loaded or is currently updating.");
        }
        catch (NoNearbyStopException ex)
        {
            var code = ex.IsOrigin ? JourneyPlanResolutionCode.NO_NEARBY_ORIGIN_STOP : JourneyPlanResolutionCode.NO_NEARBY_DESTINATION_STOP;
            return GenerateErrorResponse(code, StatusCodes.Status400BadRequest, ex.Message);
        }

        catch (Exception ex)
        {
            return GenerateErrorResponse(JourneyPlanResolutionCode.INTERNAL_ERROR, StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    private IActionResult GenerateErrorResponse(JourneyPlanResolutionCode resolutionCode, int statusCode, string detail)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = resolutionCode.ToString(),
            Detail = detail
        };
        problemDetails.Extensions["resolutionCode"] = resolutionCode.ToString();
        
        return StatusCode(statusCode, problemDetails);
    }
}

