namespace ulasim_veri_servisi.Models.Gtfs;

public class FeedInfoResponse
{
    public string? FeedVersion { get; set; }
    public DateOnly? FeedStartDate { get; set; }
    public DateOnly? FeedEndDate { get; set; }
    public DateTime DownloadedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public string? FileHash { get; set; }
    public FeedCounts Counts { get; set; } = new();
}

public class FeedCounts
{
    public int AgencyCount { get; set; }
    public int RouteCount { get; set; }
    public int StopCount { get; set; }
    public int TripCount { get; set; }
    public int StopTimeCount { get; set; }
    public int ShapePointCount { get; set; }
}

