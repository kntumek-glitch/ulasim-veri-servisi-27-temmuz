using System.Threading;
using System.Threading.Tasks;
using ulasim_veri_servisi.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services;

public interface IWalkingRouteProvider
{
    Task<WalkingResult> GetWalkingRouteAsync(double sourceLat, double sourceLon, double targetLat, double targetLon, bool includeGeometry = false, string profile = "foot", CancellationToken cancellationToken = default);
}
