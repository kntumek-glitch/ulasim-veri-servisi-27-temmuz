using System.Threading;
using System.Threading.Tasks;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services.Interfaces;

public interface IJourneyPlanningService
{
    Task<JourneyPlanSearchResponse> SearchJourneyAsync(JourneyPlanSearchRequest request, CancellationToken cancellationToken = default);
}

