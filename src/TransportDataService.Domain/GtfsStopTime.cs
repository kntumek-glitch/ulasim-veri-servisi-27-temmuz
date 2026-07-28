namespace TransportDataService.Domain;

public class GtfsStopTime
{
    public int Id { get; set; }

    public int GtfsImportRunId { get; set; }

    public GtfsImportRun GtfsImportRun { get; set; } = null!;

    public string TripId { get; set; } = string.Empty;

    public string ArrivalTimeRaw { get; set; } = string.Empty;

    public string DepartureTimeRaw { get; set; } = string.Empty;

    public int? ArrivalSeconds { get; set; }

    public int? DepartureSeconds { get; set; }

    public string StopId { get; set; } = string.Empty;

    public int StopSequence { get; set; }

    public int GtfsTripId { get; set; }

    public GtfsTrip Trip { get; set; } = null!;

    public int GtfsStopId { get; set; }

    public GtfsStop Stop { get; set; } = null!;
}
