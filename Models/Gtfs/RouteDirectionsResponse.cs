using System.Collections.Generic;

namespace ulasim_veri_servisi.Models.Gtfs;

public class RouteDirectionsResponse
{
    public string RouteId { get; set; } = string.Empty;
    public IEnumerable<DirectionInfo> Directions { get; set; } = new List<DirectionInfo>();
}

public class DirectionInfo
{
    public int DirectionId { get; set; }
    public IEnumerable<string> Headsigns { get; set; } = new List<string>();
}

