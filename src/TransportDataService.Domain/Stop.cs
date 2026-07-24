
namespace TransportDataService.Domain;

public class Stop
{
    public int Id { get; set; }

    public string ExternalStopId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Property
    public ICollection<StopRoute> StopRoutes { get; set; } = new List<StopRoute>();
}