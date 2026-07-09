using Swashbuckle.AspNetCore.Annotations;
using Microsoft.AspNetCore.Mvc;
using TransportDataService;
using ulasım_veri_servisi.Services;

namespace ulasım_veri_servisi.Controllers
{
    [ApiController]
    [Route("api/v1/import")]
    public class ImportController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ImportController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("stops")]
        [SwaggerOperation(
    Summary = "ESHOT duraklarını içe aktarır",
    Description = "CSV dosyasını okuyup durakları veritabanına aktarır."
)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult ImportStops()
        {
            var service = new CsvImportService(_context);
            var result = service.Import();

            return Ok(result);
        }
    }
}
