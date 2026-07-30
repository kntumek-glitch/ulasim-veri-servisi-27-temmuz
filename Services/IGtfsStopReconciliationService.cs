using System.Threading;

namespace ulasim_veri_servisi.Services
{
    public interface IGtfsStopReconciliationService
    {
        Task<ulasim_veri_servisi.Models.Gtfs.GtfsStopReconciliationResult> ReconcileAsync(CancellationToken cancellationToken);
    }
}

