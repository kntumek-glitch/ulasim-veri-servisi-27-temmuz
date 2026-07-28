using TransportDataService.Domain;
namespace TransportDataService.Domain;

public class GtfsImportRun
{
    public int Id { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public DateTime DownloadedAt { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FeedVersion { get; set; }

    public DateOnly? FeedStartDate { get; set; }

    public DateOnly? FeedEndDate { get; set; }

    public string? FileHash { get; set; }

    public string? ETag { get; set; }

    public DateTime? LastModified { get; set; }

    public int AgencyCount { get; set; }

    public int RouteCount { get; set; }

    public int StopCount { get; set; }

    public int TripCount { get; set; }

    public int StopTimeCount { get; set; }

    public int ShapePointCount { get; set; }

    public int FailedRecordCount { get; set; }

    public string? ErrorMessage { get; set; }

    public bool IsActive { get; set; }

    public ICollection<GtfsImportPhase> Phases { get; set; } = new List<GtfsImportPhase>();
}
