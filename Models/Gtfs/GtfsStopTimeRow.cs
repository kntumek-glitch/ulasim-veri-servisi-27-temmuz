namespace ulasim_veri_servisi.Models.Gtfs;

public class GtfsStopTimeRow
{
    public string trip_id { get; set; } = "";
    public string arrival_time { get; set; } = "";
    public string departure_time { get; set; } = "";
    public string stop_id { get; set; } = "";
    public int stop_sequence { get; set; }
    public int? timepoint { get; set; }
}

