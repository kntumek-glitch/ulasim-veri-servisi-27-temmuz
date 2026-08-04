namespace ulasim_veri_servisi.Models.Gtfs.JourneyPlan;

public class WalkingResult
{
    public ErrorState State { get; set; } = ErrorState.Success();
    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public string? EncodedPolyline { get; set; }
    public object? GeometryGeoJson { get; set; }
}
