namespace ulasim_veri_servisi.Models;

public class GtfsImportResult
{
    public string Status { get; set; } = "";

    public bool IsNewFeed { get; set; }

    public string? FileHash { get; set; }

    public int RouteCount { get; set; }

    public int StopCount { get; set; }

    public int TripCount { get; set; }

    public int StopTimeCount { get; set; }

    public int FailedRecordCount { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}


