namespace TransportDataService.Domain;

public class GtfsAgency
{
    public int Id { get; set; }

    public int GtfsImportRunId { get; set; }

    public GtfsImportRun GtfsImportRun { get; set; } = null!;

    public string AgencyId { get; set; } = string.Empty;

    public string AgencyName { get; set; } = string.Empty;

    public string AgencyUrl { get; set; } = string.Empty;

    public string AgencyTimezone { get; set; } = string.Empty;

    public string? AgencyLang { get; set; }

    public string? AgencyPhone { get; set; }


}