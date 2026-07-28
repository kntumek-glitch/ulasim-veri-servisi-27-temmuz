using System.Threading;

namespace ulasım_veri_servisi.Services
{
    public interface IGtfsStopReconciliationService
    {
        Task<ulasım_veri_servisi.Models.Gtfs.GtfsStopReconciliationResult> ReconcileAsync(CancellationToken cancellationToken);
    }
}
