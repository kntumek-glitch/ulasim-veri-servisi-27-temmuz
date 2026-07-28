using System.ComponentModel.DataAnnotations.Schema;
namespace TransportDataService.Domain;

public class GtfsTrip
{
    public int Id { get; set; }

    public int GtfsImportRunId { get; set; }

    [ForeignKey(nameof(GtfsImportRunId))]
    public GtfsImportRun GtfsImportRun { get; set; } = null!;

    public string TripId { get; set; } = string.Empty;

    public string RouteId { get; set; } = string.Empty;

    public string ServiceId { get; set; } = string.Empty;

    public string? ShapeId { get; set; }

    public string? TripHeadsign { get; set; }

    public int? DirectionId { get; set; }

    public int GtfsRouteId { get; set; }

    [ForeignKey(nameof(GtfsRouteId))]
    public GtfsRoute Route { get; set; } = null!;

    public ICollection<GtfsStopTime> StopTimes { get; set; }
        = new List<GtfsStopTime>();
}