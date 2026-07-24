using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using ulasım_veri_servisi.Services;

namespace ulasım_veri_servisi.Controllers
{
    [ApiController]
    [Route("api/v1/routes")]
    public class RoutesController : ControllerBase
    {
        private readonly RouteVehiclesService _routeVehiclesService;

        public RoutesController(RouteVehiclesService routeVehiclesService)
        {
            _routeVehiclesService = routeVehiclesService;
        }

        [HttpGet("{routeNumber}/vehicles")]
        [SwaggerOperation(
         Summary = "Hat üzerindeki araçları getirir",
         Description = "Verilen hat numarasına göre ESHOT API'den araç konumlarını getirir."
     )]
        [ProducesResponseType(typeof(RouteVehiclesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetRouteVehicles(string routeNumber)
        {
            var result = await _routeVehiclesService.GetRouteVehiclesAsync(routeNumber);

            return Ok(result);
        }
    }
}