using TransportDataService.Domain;

namespace ulasim_veri_servisi.Models.Gtfs;

public class GtfsImportRunResponse
{
    public int Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public double? DurationSeconds { get; init; }
    public string? FileHash { get; init; }
    public string? ErrorMessage { get; init; }
    public GtfsImportDataCounts DataCounts { get; init; } = new();
    public ICollection<GtfsImportPhaseResponse> Phases { get; init; } = new List<GtfsImportPhaseResponse>();

    public static GtfsImportRunResponse From(GtfsImportRun run) => new()
    {
        Id = run.Id,
        Status = run.Status,
        IsActive = run.IsActive,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        DurationSeconds = run.FinishedAt is null ? null : Math.Round((run.FinishedAt.Value - run.StartedAt).TotalSeconds, 3),
        FileHash = run.FileHash,
        ErrorMessage = run.ErrorMessage,
        Phases = run.Phases?.Select(GtfsImportPhaseResponse.From).ToList() ?? new List<GtfsImportPhaseResponse>(),
        DataCounts = new GtfsImportDataCounts
        {
            AgencyCount = run.AgencyCount,
            RouteCount = run.RouteCount,
            StopCount = run.StopCount,
            TripCount = run.TripCount,
            StopTimeCount = run.StopTimeCount,
            ShapePointCount = run.ShapePointCount,
            FailedRecordCount = run.FailedRecordCount
        }
    };
}

public class GtfsImportDataCounts
{
    public int AgencyCount { get; init; }
    public int RouteCount { get; init; }
    public int StopCount { get; init; }
    public int TripCount { get; init; }
    public int StopTimeCount { get; init; }
    public int ShapePointCount { get; init; }
    public int FailedRecordCount { get; init; }
}

public class GtfsImportPhaseResponse
{
    public string PhaseName { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public double? DurationSeconds { get; init; }
    public int ProgressPercentage { get; init; }
    public int ProcessedRecordCount { get; init; }
    public string? ErrorMessage { get; init; }

    public static GtfsImportPhaseResponse From(GtfsImportPhase phase) => new()
    {
        PhaseName = phase.PhaseName,
        StartedAt = phase.StartedAt,
        FinishedAt = phase.FinishedAt,
        DurationSeconds = phase.FinishedAt.HasValue ? Math.Round((phase.FinishedAt.Value - phase.StartedAt).TotalSeconds, 3) : null,
        ProgressPercentage = phase.ProgressPercentage,
        ProcessedRecordCount = phase.ProcessedRecordCount,
        ErrorMessage = phase.ErrorMessage
    };
}


