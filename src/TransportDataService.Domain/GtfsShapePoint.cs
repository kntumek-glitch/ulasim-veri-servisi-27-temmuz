namespace TransportDataService.Domain;

public class GtfsShapePoint
{
    public int Id { get; set; }

    public int GtfsImportRunId { get; set; }
    public GtfsImportRun GtfsImportRun { get; set; } = null!;

    public string ShapeId { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int Sequence { get; set; }
}