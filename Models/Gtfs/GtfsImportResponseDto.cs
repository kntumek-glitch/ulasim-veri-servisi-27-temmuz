using TransportDataService.Domain;

namespace ulasim_veri_servisi.Models.Gtfs;

public class GtfsImportCountersDto
{
    public int AgencyCount { get; set; }
    public int RouteCount { get; set; }
    public int StopCount { get; set; }
    public int TripCount { get; set; }
    public int StopTimeCount { get; set; }
    public int ShapePointCount { get; set; }
    public int FailedRecordCount { get; set; }
}

public class GtfsImportResponseDto
{
    public int ImportRunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsNewFeed { get; set; }
    public int? PreviousSuccessfulImportId { get; set; }
    public string? FileHash { get; set; }
    public GtfsImportCountersDto Counters { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Reason { get; set; }
    public ICollection<GtfsImportPhaseResponse> Phases { get; set; } = new List<GtfsImportPhaseResponse>();

    public static GtfsImportResponseDto FromRun(GtfsImportRun run, int? previousSuccessfulId = null)
    {
        return new GtfsImportResponseDto
        {
            ImportRunId = run.Id,
            Status = run.Status,
            IsNewFeed = run.Status != "Skipped",
            PreviousSuccessfulImportId = previousSuccessfulId,
            FileHash = run.FileHash,
            Counters = new GtfsImportCountersDto
            {
                AgencyCount = run.AgencyCount,
                RouteCount = run.RouteCount,
                StopCount = run.StopCount,
                TripCount = run.TripCount,
                StopTimeCount = run.StopTimeCount,
                ShapePointCount = run.ShapePointCount,
                FailedRecordCount = run.FailedRecordCount
            },
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Reason = run.ErrorMessage,
            Phases = run.Phases?.Select(GtfsImportPhaseResponse.From).ToList() ?? new List<GtfsImportPhaseResponse>()
        };
    }
}

