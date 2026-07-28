namespace TransportDataService.Domain;

public class GtfsCalendarDate
{
    public int Id { get; set; }

    public int GtfsImportRunId { get; set; }

    public GtfsImportRun GtfsImportRun { get; set; } = null!;

    public string ServiceId { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public int ExceptionType { get; set; }
}