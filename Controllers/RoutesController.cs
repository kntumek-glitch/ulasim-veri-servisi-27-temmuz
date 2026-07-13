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
     Summary = "Hattaki aktif araç konumlarını getirir",
     Description = "Belirtilen hatta çalışan aktif otobüslerin konumlarını döndürür."
 )]
        [ProducesResponseType(typeof(RouteVehiclesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]

        public async Task<IActionResult> GetRouteVehicles([FromRoute] string routeNumber)
        {
            try
            {
                var result = await _routeVehiclesService.GetRouteVehiclesAsync(routeNumber);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}