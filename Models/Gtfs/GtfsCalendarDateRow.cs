namespace ulasım_veri_servisi.Models.Gtfs;

public class GtfsCalendarDateRow
{
    public string service_id { get; set; } = "";

    public string date { get; set; } = "";

    public int exception_type { get; set; }
}