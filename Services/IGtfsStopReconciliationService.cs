using System.Threading;

namespace ulasım_veri_servisi.Services
{
    public interface IGtfsStopReconciliationService
    {
        Task ReconcileAsync(CancellationToken cancellationToken);
    }
}
