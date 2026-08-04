using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ulasim_veri_servisi.Models.Routing;
using ulasim_veri_servisi.Services;

namespace ulasim_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/routing")]
public class RoutingController : ControllerBase
{
    private readonly WalkingRoutingService _walkingRoutingService;

    public RoutingController(WalkingRoutingService walkingRoutingService)
    {
        _walkingRoutingService = walkingRoutingService;
    }

    /// <summary>
    /// Calculates a pedestrian routing path between two coordinates.
    /// </summary>
    [HttpPost("walk")]
    public async Task<IActionResult> GetWalkingRoute([FromBody] WalkRoutingRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null)
            return BadRequest(new ProblemDetails { Title = "Geçersiz İstek", Detail = "İstek gövdesi boş olamaz.", Status = 400 });

        if (request.Origin == null || request.Destination == null)
            return BadRequest(new ProblemDetails { Title = "Geçersiz Koordinatlar", Detail = "Origin ve Destination koordinatları zorunludur.", Status = 400 });

        if (!IsValidCoordinate(request.Origin.Lat, request.Origin.Lon))
            return BadRequest(new ProblemDetails { Title = "Sınır İhlali", Detail = "Origin koordinatları geçerli sınırlar dışında (Lat: -90..90, Lon: -180..180).", Status = 400 });

        if (!IsValidCoordinate(request.Destination.Lat, request.Destination.Lon))
            return BadRequest(new ProblemDetails { Title = "Sınır İhlali", Detail = "Destination koordinatları geçerli sınırlar dışında (Lat: -90..90, Lon: -180..180).", Status = 400 });

        var result = await _walkingRoutingService.CalculateWalkingRouteAsync(
            request.Origin.Lat, request.Origin.Lon,
            request.Destination.Lat, request.Destination.Lon,
            request.IncludeGeometry,
            cancellationToken);

        if (!result.State.IsSuccess)
        {
            var statusCode = result.State.ErrorCode == "NO_ROUTE" ? 404 : 
                             result.State.ErrorCode == "UNROUTABLE_LOCATION" ? 400 : 502;
            
            return StatusCode(statusCode, new ProblemDetails
            {
                Title = "Yönlendirme Hatası",
                Detail = result.State.ErrorMessage,
                Status = statusCode
            });
        }

        var response = new WalkRoutingResponseDto
        {
            DistanceMeters = result.DistanceMeters,
            DurationSeconds = result.DurationSeconds,
            Source = "OSRM",
            IsApproximate = false, // OSRM gives exact routed values
            RetrievedAt = DateTimeOffset.UtcNow
        };

        if (request.IncludeGeometry)
        {
            response.Geometry = result.GeometryGeoJson;
        }

        return Ok(response);
    }

    private bool IsValidCoordinate(double lat, double lon)
    {
        return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
    }
}
