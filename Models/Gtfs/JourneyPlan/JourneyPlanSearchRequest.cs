using System;
using System.ComponentModel.DataAnnotations;

namespace TransportDataService.Models.Gtfs.JourneyPlan;

public class JourneyPlanSearchRequest
{
    [Required]
    public CoordinateDto Origin { get; set; } = null!;

    [Required]
    public CoordinateDto Destination { get; set; } = null!;

    [Required]
    public DateTimeOffset DepartureDateTime { get; set; }

    [Range(0, 1, ErrorMessage = "Şu anda sistem yalnızca 0 (Aktarmasız) veya 1 aktarmalı rotaları desteklemektedir.")]
    public int MaxTransfers { get; set; } = 1;

    [Range(100, 5000, ErrorMessage = "Yürüyüş mesafesi 100 ile 5000 metre arasında olmalıdır.")]
    public int MaxWalkingMeters { get; set; } = 1500;

    [Range(1, 50, ErrorMessage = "Sonuç limiti 1 ile 50 arasında olmalıdır.")]
    public int MaxResults { get; set; } = 10;
}
