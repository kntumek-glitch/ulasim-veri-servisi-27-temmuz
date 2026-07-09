namespace TransportDataService.Domain;

public class StopRoute
{
    public int Id { get; set; }

    public int StopId { get; set; }

    public string RouteNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Navigation Property
    public Stop Stop { get; set; } = null!;
}