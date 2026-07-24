using ulasım_veri_servisi.Models.Gtfs;
namespace ulasım_veri_servisi.Models.Gtfs;

public class GtfsAgencyRow
{
    public string? agency_id { get; set; }

    public string agency_name { get; set; } = string.Empty;

    public string agency_url { get; set; } = string.Empty;

    public string agency_timezone { get; set; } = string.Empty;

    public string? agency_lang { get; set; }

    public string? agency_phone { get; set; }
}