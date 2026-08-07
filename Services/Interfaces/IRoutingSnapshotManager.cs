using System.Threading;
using System.Threading.Tasks;
using ulasim_veri_servisi.Models.Routing;

namespace ulasim_veri_servisi.Services.Interfaces;

public interface IRoutingSnapshotManager
{
    RoutingSnapshot? GetActiveSnapshot();
    Task BuildAndSwapSnapshotAsync(int importRunId, string feedHash, CancellationToken cancellationToken);
}
