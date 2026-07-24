namespace ulasım_veri_servisi.Models.Gtfs;

public class GtfsStopRow
{
    public string stop_id { get; set; } = "";

    public string stop_name { get; set; } = "";

    public double stop_lat { get; set; }

    public double stop_lon { get; set; }

    public string? stop_code { get; set; }

    public string? stop_desc { get; set; }

    public string? platform_code { get; set; }

    public int? location_type { get; set; }

    public string? parent_station { get; set; }

    public string? zone_id { get; set; }

    public string? stop_url { get; set; }
}