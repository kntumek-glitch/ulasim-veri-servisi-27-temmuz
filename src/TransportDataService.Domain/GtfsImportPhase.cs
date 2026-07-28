namespace TransportDataService.Domain;

public class GtfsImportPhase
{
    public int Id { get; set; }
    
    public int GtfsImportRunId { get; set; }
    
    public string PhaseName { get; set; } = string.Empty;
    
    public DateTime StartedAt { get; set; }
    
    public DateTime? FinishedAt { get; set; }
    
    public int ProgressPercentage { get; set; }
    
    public int ProcessedRecordCount { get; set; }
    
    public string? ErrorMessage { get; set; }

    public GtfsImportRun? GtfsImportRun { get; set; }
}
