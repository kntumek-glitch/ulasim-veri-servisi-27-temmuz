using System.Threading;
using System.Threading.Tasks;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace ulasım_veri_servisi.Services.Interfaces;

public interface IJourneyPlanningService
{
    Task<JourneyPlanSearchResponse> SearchJourneyAsync(JourneyPlanSearchRequest request, CancellationToken cancellationToken = default);
}
