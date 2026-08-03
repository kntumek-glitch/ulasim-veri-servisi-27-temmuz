using Microsoft.AspNetCore.Mvc;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Controllers
{
    [ApiController]
    [Route("api/v1/gtfs/routes")]
    [ServiceFilter(typeof(ulasim_veri_servisi.Filters.GtfsETagCacheFilterAttribute))]
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
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                return Problem(detail: "RouteId boş olamaz.", statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz İstek");

            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                return Problem(detail: "Geçersiz tarih formatı. Lütfen tam olarak YYYY-MM-DD formatında (örneğin: 2024-01-01) gönderin.", statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz İstek");
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var result = await _routeDeparturesService.GetRouteDeparturesAsync(routeId, directionId, parsedDate, page, pageSize, cancellationToken);

            if (result == null)
            {
                return Problem(detail: $"Belirtilen routeId ({routeId}) bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak Bulunamadı");
            }

            return Ok(result);
        }
    }
}

