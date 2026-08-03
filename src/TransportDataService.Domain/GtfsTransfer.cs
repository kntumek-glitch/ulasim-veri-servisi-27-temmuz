using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TransportDataService.Domain;

public class GtfsTransfer
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int GtfsImportRunId { get; set; }

    [Required]
    [MaxLength(50)]
    public string FromStopId { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string ToStopId { get; set; } = null!;

    public double DistanceMeters { get; set; }

    public int WalkingTimeSeconds { get; set; }

    public bool IsSamePhysicalStop { get; set; }

    public bool IsSameParentStation { get; set; }

    public bool IsSameCoordinateCluster { get; set; }

    [MaxLength(50)]
    public string CalculationMethod { get; set; } = "Haversine";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(GtfsImportRunId))]
    public GtfsImportRun GtfsImportRun { get; set; } = null!;
}
