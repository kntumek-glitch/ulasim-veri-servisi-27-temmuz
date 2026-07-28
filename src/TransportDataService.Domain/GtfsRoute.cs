namespace TransportDataService.Domain;

public class GtfsRoute
{
    public int Id { get; set; }

    public int GtfsImportRunId { get; set; }

    public GtfsImportRun GtfsImportRun { get; set; } = null!;

    public string RouteId { get; set; } = string.Empty;

    public string AgencyId { get; set; } = string.Empty;

    public string RouteShortName { get; set; } = string.Empty;

    public string RouteLongName { get; set; } = string.Empty;

    public int? RouteType { get; set; }

    public string? RouteColor { get; set; }

    public string? RouteTextColor { get; set; }

 

    public string? RouteDesc { get; set; }

   

   

    public ICollection<GtfsTrip> Trips { get; set; }
        = new List<GtfsTrip>();
}