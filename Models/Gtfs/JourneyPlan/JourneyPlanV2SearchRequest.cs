using System;
using System.ComponentModel.DataAnnotations;

namespace TransportDataService.Models.Gtfs.JourneyPlan;

public class JourneyPlanV2SearchRequest
{
    [Required]
    public CoordinateDto Origin { get; set; } = null!;

    [Required]
    public CoordinateDto Destination { get; set; } = null!;

    [Required(ErrorMessage = "Geçerli bir tarih ve saat (dateTime) belirtilmelidir.")]
    public DateTimeOffset? DateTime { get; set; }

    public RoutingMode SearchMode { get; set; } = RoutingMode.DEPART_AT;

    [Range(0, 2, ErrorMessage = "Sistem en fazla 2 aktarmalı (toplam 3 bacak) rotaları desteklemektedir.")]
    public int MaxTransfers { get; set; } = 1;

    [Range(100, 5000, ErrorMessage = "Yürüyüş mesafesi 100 ile 5000 metre arasında olmalıdır.")]
    public int MaxWalkingMeters { get; set; } = 1500;

    [Range(1, 50, ErrorMessage = "Sonuç limiti 1 ile 50 arasında olmalıdır.")]
    public int MaxResults { get; set; } = 10;

    public bool IncludeIntermediateStops { get; set; } = false;
    
    public bool IncludeWalkingGeometry { get; set; } = false;
}
