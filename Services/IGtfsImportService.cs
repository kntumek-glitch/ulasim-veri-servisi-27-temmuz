using TransportDataService.Domain;

namespace ulasim_veri_servisi.Services;

public interface IGtfsImportService
{
    Task<GtfsImportRun> ImportAsync(
        CancellationToken cancellationToken);
        
    Task CleanupOldFeedsAsync(CancellationToken cancellationToken);
}
