using Microsoft.AspNetCore.Mvc;
using ulasım_veri_servisi.Services.Interfaces;

namespace ulasım_veri_servisi.Controllers
{
    [ApiController]
    [Route("api/v1/gtfs/trips")]
    [ServiceFilter(typeof(ulasım_veri_servisi.Filters.GtfsETagCacheFilterAttribute))]
    public class GtfsTripsController : ControllerBase
    {
        private readonly ITripStopsService _tripStopsService;

        public GtfsTripsController(ITripStopsService tripStopsService)
        {
            _tripStopsService = tripStopsService;
        }

        [HttpGet("{tripId}/stops")]
        public async Task<IActionResult> GetTripStops(string tripId)
        {
            if (string.IsNullOrWhiteSpace(tripId))
                return Problem(detail: "TripId boş olamaz.", statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz İstek");

            var result = await _tripStopsService.GetTripStopsAsync(tripId);

            if (result == null)
                return Problem(detail: $"Belirtilen tripId ({tripId}) bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak Bulunamadı");

            return Ok(result);
        }
    }
}
