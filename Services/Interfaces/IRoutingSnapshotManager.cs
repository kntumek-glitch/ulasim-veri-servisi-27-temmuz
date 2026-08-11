using System.Threading;
using System.Threading.Tasks;
using ulasim_veri_servisi.Models.Routing;

namespace ulasim_veri_servisi.Services.Interfaces;

public interface IRoutingSnapshotManager
{
    RoutingSnapshot? GetActiveSnapshot();
    Task<RoutingSnapshot> BuildCandidateSnapshotAsync(int importRunId, string feedHash, CancellationToken cancellationToken);
    void PromoteSnapshot(RoutingSnapshot candidate);
}
