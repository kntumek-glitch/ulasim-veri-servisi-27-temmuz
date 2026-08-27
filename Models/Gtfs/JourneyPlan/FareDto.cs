using System.Collections.Generic;

namespace TransportDataService.Models.Gtfs.JourneyPlan;

public class FareDto
{
    public double TotalFare { get; set; }
    public string Currency { get; set; } = "TRY";
    public string FareType { get; set; } = "Tam"; // e.g. Tam, Öğrenci
    public List<FareLegDto> Breakdown { get; set; } = new();
}

public class FareLegDto
{
    public string LegId { get; set; } = string.Empty; // To tie it back to a specific leg/route
    public string RouteShortName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public bool IsTransfer { get; set; }
    public string Description { get; set; } = string.Empty; // e.g. "İlk Biniş", "1. Aktarma"
}
