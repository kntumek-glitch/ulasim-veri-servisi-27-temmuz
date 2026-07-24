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
        private readonly CsvImportService _csvImportService;
        public ImportController(CsvImportService csvImportService)
        {
            _csvImportService = csvImportService;
        }

        [HttpPost("stops")]
        [SwaggerOperation(
    Summary = "ESHOT duraklarını içe aktarır",
    Description = "CSV dosyasını okuyup durakları veritabanına aktarır."
)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [ServiceFilter(typeof(ulasım_veri_servisi.Filters.AdminKeyAuthAttribute))]
        public async Task<IActionResult> ImportStops(
    CancellationToken cancellationToken)
        {
            var result = await _csvImportService.ImportAsync(cancellationToken);

            return Ok(result);
        }
    }
}
