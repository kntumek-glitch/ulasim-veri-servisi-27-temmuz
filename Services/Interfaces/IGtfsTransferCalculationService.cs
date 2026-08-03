using System.Threading;
using System.Threading.Tasks;

namespace ulasim_veri_servisi.Services.Interfaces;

public interface IGtfsTransferCalculationService
{
    Task CalculateTransfersAsync(int gtfsImportRunId, CancellationToken cancellationToken);
}
