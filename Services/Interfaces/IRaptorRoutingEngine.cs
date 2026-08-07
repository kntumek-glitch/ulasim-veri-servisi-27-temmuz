using System.Threading;
using System.Threading.Tasks;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services.Interfaces;

public interface IRaptorRoutingEngine
{
    Task<JourneyPlanSearchResponse> SearchJourneyV2Async(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken);
}
