namespace ulasim_veri_servisi.Models.Routing;

public struct RouteLabel
{
    public string StopId { get; set; }
    public int StopIndex { get; set; }
    public int AbsoluteArrivalSeconds { get; set; }
    public int Round { get; set; }
    public int TotalWalkDurationSeconds { get; set; }
    public int TotalWaitDurationSeconds { get; set; }
    
    // Path Reconstruction
    public string PreviousStopId { get; set; }
    public string PreviousTripId { get; set; }
    public string PreviousPatternId { get; set; }
    public string BoardingStopId { get; set; }
    public int BoardingStopPatternIndex { get; set; }
    public int AlightingStopPatternIndex { get; set; }
    public bool UsedTransferEdge { get; set; }
}

public struct BackwardRouteLabel
{
    public string StopId { get; set; }
    public int StopIndex { get; set; }
    public int AbsoluteDepartureSeconds { get; set; } // We want to maximize this
    public int Round { get; set; }
    public int TotalWalkDurationSeconds { get; set; }
    public int TotalWaitDurationSeconds { get; set; }
    
    // Path Reconstruction (Backwards)
    public string NextStopId { get; set; }
    public string NextTripId { get; set; }
    public string NextPatternId { get; set; }
    public string AlightingStopId { get; set; }
    public int BoardingStopPatternIndex { get; set; }
    public int AlightingStopPatternIndex { get; set; }
    public bool UsedTransferEdge { get; set; }
}
