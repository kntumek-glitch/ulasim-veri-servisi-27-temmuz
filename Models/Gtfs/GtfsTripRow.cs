namespace ulasim_veri_servisi.Models.Gtfs;

public class GtfsTripRow
{
    public string route_id { get; set; } = "";
    public string service_id { get; set; } = "";
    public string trip_id { get; set; } = "";
    public int? direction_id { get; set; }
    public int? wheelchair_accessible { get; set; }
    public int? bikes_allowed { get; set; }
    public string? shape_id { get; set; }
    public string? trip_headsign { get; set; }
}

