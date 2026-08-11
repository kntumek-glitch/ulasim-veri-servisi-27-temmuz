using System.Collections.Generic;
using System.Linq;
using TransportDataService.Domain;

namespace ulasim_veri_servisi.Services.JourneyPlanning.Models;

public class ActiveStopsCache
{
    public List<GtfsStop> Stops { get; set; } = new();
    public Dictionary<string, List<GtfsStop>> SpatialGrid { get; set; } = new();
    public Dictionary<string, List<GtfsTransfer>> TransfersByStopId { get; set; } = new();
    public ILookup<string, GtfsTransfer> TransfersByToStopId { get; set; } = default!;
}
