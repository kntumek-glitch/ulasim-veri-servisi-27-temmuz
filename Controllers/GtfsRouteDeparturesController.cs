using Microsoft.AspNetCore.Mvc;
using ulasım_veri_servisi.Services.Interfaces;

namespace ulasım_veri_servisi.Controllers
{
    [ApiController]
    [Route("api/v1/gtfs/routes")]
    [ServiceFilter(typeof(ulasım_veri_servisi.Filters.GtfsETagCacheFilterAttribute))]
    public class GtfsRouteDeparturesController : ControllerBase
    {
        private readonly IRouteDeparturesService _routeDeparturesService;

        public GtfsRouteDeparturesController(IRouteDeparturesService routeDeparturesService)
        {
            _routeDeparturesService = routeDeparturesService;
        }

        [HttpGet("{routeId}/departures")]
        public async Task<IActionResult> GetRouteDepartures(
            string routeId, 
            [FromQuery] int directionId, 
            [FromQuery] string date, 
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 20)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                return BadRequest(new { Message = "RouteId boş olamaz." });

            if (!DateOnly.TryParse(date, out var parsedDate))
            {
                return BadRequest(new { Message = "Geçersiz tarih formatı. Lütfen YYYY-MM-DD formatında gönderin." });
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var result = await _routeDeparturesService.GetRouteDeparturesAsync(routeId, directionId, parsedDate, page, pageSize);

            if (result == null)
            {
                return NotFound(new { Message = $"Belirtilen routeId ({routeId}) bulunamadı." });
            }

            return Ok(result);
        }
    }
}
