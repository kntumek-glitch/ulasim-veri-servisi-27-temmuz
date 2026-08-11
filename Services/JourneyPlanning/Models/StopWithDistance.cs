namespace ulasim_veri_servisi.Services.JourneyPlanning.Models;

using TransportDataService.Domain;

public class StopWithDistance
{
    public GtfsStop Stop { get; set; } = null!;
    public int DistanceMeters { get; set; }
    public int WalkingTimeSeconds { get; set; }
}
