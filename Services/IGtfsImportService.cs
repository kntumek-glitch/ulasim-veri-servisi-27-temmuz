using TransportDataService.Domain;

namespace ulasım_veri_servisi.Services;

public interface IGtfsImportService
{
    Task<GtfsImportRun> ImportAsync(
        CancellationToken cancellationToken);
}