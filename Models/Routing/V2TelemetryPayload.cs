using System;

namespace ulasim_veri_servisi.Models.Routing;

public class V2TelemetryPayload
{
    // Request Info
    public string SearchMode { get; set; } = string.Empty;

    // Spatial Prep
    public int OriginCandidateStopCount { get; set; }
    public int DestinationCandidateStopCount { get; set; }

    // Algorithm Complexity
    public int RoundCount { get; set; }
    public int PatternScannedCount { get; set; }
    public int TripScannedCount { get; set; }
    public int LabelUpdateCount { get; set; }
    public int TransferRelaxationCount { get; set; }

    // Outcomes
    public int ResultCount { get; set; }
    public string ReasonCode { get; set; } = "OK";
    public long CalculationDurationMs { get; set; }

    // Data Context
    public int FeedImportId { get; set; }
    public string FeedHash { get; set; } = string.Empty;

    // Pedestrian Activity
    public int ExactWalkingLegCount { get; set; }
    public int ApproximateWalkingLegCount { get; set; }
}
