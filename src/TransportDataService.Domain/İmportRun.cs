namespace TransportDataService.Domain;

public class ImportRun
{
    public int Id { get; set; }

    public string SourceName { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int ImportedRecordCount { get; set; }

    public int UpdatedRecordCount { get; set; }

    public int FailedRecordCount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}
