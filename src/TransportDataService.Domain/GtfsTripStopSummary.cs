using System.ComponentModel.DataAnnotations.Schema;

namespace TransportDataService.Domain;

public class GtfsTripStopSummary
{
    public int Id { get; set; }

    public int GtfsImportRunId { get; set; }

    [ForeignKey(nameof(GtfsImportRunId))]
    public GtfsImportRun GtfsImportRun { get; set; } = null!;

    public int GtfsTripId { get; set; }

    [ForeignKey(nameof(GtfsTripId))]
    public GtfsTrip Trip { get; set; } = null!;

    [Column(TypeName = "integer[]")]
    public List<int> StopSequences { get; set; } = new List<int>();
}
