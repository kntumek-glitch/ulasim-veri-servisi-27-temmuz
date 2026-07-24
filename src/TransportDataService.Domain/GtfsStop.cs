namespace TransportDataService.Domain;

public class GtfsStop
{
    public int Id { get; set; }

    public string StopId { get; set; } = string.Empty;

    public string StopCode { get; set; } = string.Empty;

    public string StopName { get; set; } = string.Empty;

    public double StopLat { get; set; }

    public double StopLon { get; set; }

    public string? StopDesc { get; set; }

    public string? ZoneId { get; set; }

    public string? StopUrl { get; set; }

    public int? LocationType { get; set; }

    public string? ParentStation { get; set; }

    public string? PlatformCode { get; set; } 

    public ICollection<GtfsStopTime> StopTimes { get; set; }
        = new List<GtfsStopTime>();
}