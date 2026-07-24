namespace TransportDataService.Domain;

public class GtfsCalendarDate
{
    public int Id { get; set; }

    public string ServiceId { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public int ExceptionType { get; set; }
}