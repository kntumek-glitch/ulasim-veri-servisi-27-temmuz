namespace ulasım_veri_servisi.Models.Gtfs;

public class GtfsShapePointRow
{
    public string shape_id { get; set; } = "";
    public double shape_pt_lat { get; set; }
    public double shape_pt_lon { get; set; }
    public int shape_pt_sequence { get; set; }
}