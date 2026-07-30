namespace ulasim_veri_servisi.Models.Gtfs;

public class RouteStopDto
{
    public string StopId { get; set; } = string.Empty;
    public string StopCode { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int StopSequence { get; set; }
}

