namespace ulasim_veri_servisi.Models.Gtfs;

public class RouteDto
{
    public string RouteId { get; set; } = string.Empty;
    public string AgencyId { get; set; } = string.Empty;
    public string RouteShortName { get; set; } = string.Empty;
    public string RouteLongName { get; set; } = string.Empty;
    public string? RouteDesc { get; set; }
    public int? RouteType { get; set; }
    public string? RouteColor { get; set; }
    public string? RouteTextColor { get; set; }
}

