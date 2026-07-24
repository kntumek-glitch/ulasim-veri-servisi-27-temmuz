namespace ulasım_veri_servisi.Models.Gtfs;

public class RouteDto
{
    public string RouteId { get; set; } = string.Empty;
    public string RouteShortName { get; set; } = string.Empty;
    public string RouteLongName { get; set; } = string.Empty;
    public int? RouteType { get; set; }
}
