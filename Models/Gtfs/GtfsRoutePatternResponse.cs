namespace ulasım_veri_servisi.Models.Gtfs;

public class GtfsRoutePatternResponse
{
    public string PatternId { get; init; } = string.Empty;
    public string RouteId { get; init; } = string.Empty;
    public int DirectionId { get; init; }
    public string RepresentativeTripId { get; init; } = string.Empty;
    public string? ShapeId { get; init; }
    public int TripCount { get; init; }
    public int StopCount { get; init; }
    public GtfsPatternEndpointStop StartStop { get; init; } = new();
    public GtfsPatternEndpointStop EndStop { get; init; } = new();
}

public class GtfsPatternEndpointStop
{
    public string StopId { get; init; } = string.Empty;
    public string StopCode { get; init; } = string.Empty;
    public string StopName { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? PlatformCode { get; init; }
}

