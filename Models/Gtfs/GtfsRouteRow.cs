namespace ulasım_veri_servisi.Models.Gtfs;

public class GtfsRouteRow
{
    public string route_id { get; set; } = "";

    public string route_short_name { get; set; } = "";

    public string route_long_name { get; set; } = "";

    public int route_type { get; set; }
}
