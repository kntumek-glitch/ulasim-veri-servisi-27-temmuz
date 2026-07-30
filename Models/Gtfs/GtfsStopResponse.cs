namespace ulasim_veri_servisi.Models.Gtfs;

public class GtfsStopResponse
{
    public string StopId { get; init; } = string.Empty;
    public string StopCode { get; init; } = string.Empty;
    public string StopName { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? PlatformCode { get; init; }
    public int? LocationType { get; init; }
    public string? ParentStation { get; init; }
    public IReadOnlyCollection<int> DirectionIds { get; init; } = Array.Empty<int>();
}

public class GtfsStopRouteResponse
{
    public string RouteId { get; init; } = string.Empty;
    public string? RouteShortName { get; init; }
    public string? RouteLongName { get; init; }
    public int? DirectionId { get; init; }
    public string? TripHeadsign { get; init; }
}



