namespace ulasim_veri_servisi.Services.JourneyPlanning.Models;

public class DirectTripResult
{
    public string TripId { get; set; } = null!;
    public string RouteId { get; set; } = null!;
    public string RouteShortName { get; set; } = null!;
    public int? RouteType { get; set; }
    public string? TripHeadsign { get; set; }
    public int? DirectionId { get; set; }
    public string OriginStopId { get; set; } = null!;
    public string DestStopId { get; set; } = null!;
    public int OriginStopSequence { get; set; }
    public int DestStopSequence { get; set; }
    public int DepartureSeconds { get; set; }
    public string DepartureTimeRaw { get; set; } = null!;
    public int ArrivalSeconds { get; set; }
    public string ArrivalTimeRaw { get; set; } = null!;
    public bool IsPreviousDayTrip { get; set; }
    public int StopCount { get; set; }
    public string ServiceId { get; set; } = null!;
    public string? ShapeId { get; set; }
}

public class Leg1TripData { public string TripId { get; set; } = null!; public int TripDbId { get; set; } public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? TripHeadsign { get; set; } public int? DirectionId { get; set; } public string OriginStopId { get; set; } = null!; public int DepSeq { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } }
public class Leg1StopData { public Leg1TripData TripInfo { get; set; } = null!; public string TransferStop1Id { get; set; } = null!; public int ArrSeq { get; set; } public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public int StopCount { get; set; } }
public class Leg2TripData { public string TripId { get; set; } = null!; public int TripDbId { get; set; } public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? TripHeadsign { get; set; } public int? DirectionId { get; set; } public string TransferStop2Id { get; set; } = null!; public string DestStopId { get; set; } = null!; public int DepSeq { get; set; } public int ArrSeq { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public int StopCount { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } }
public class TransferPair { public string TransferStop1Id { get; set; } = null!; public string TransferStop2Id { get; set; } = null!; public int WalkSeconds { get; set; } }

public class LegData { public string TripId { get; set; } = null!; public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? Headsign { get; set; } public int? DirectionId { get; set; } public string FromStopId { get; set; } = null!; public string ToStopId { get; set; } = null!; public int FromStopSequence { get; set; } public int ToStopSequence { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public int StopCount { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } public string ServiceDate { get; set; } = null!; public string PatternId { get; set; } = null!; }
public class OneTransferResult { public LegData Leg1 { get; set; } = null!; public LegData Leg2 { get; set; } = null!; public int TransferWalkMeters { get; set; } public int TransferWalkSeconds { get; set; } }

public class Leg3TripData { public string TripId { get; set; } = null!; public int TripDbId { get; set; } public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? TripHeadsign { get; set; } public int? DirectionId { get; set; } public string DestStopId { get; set; } = null!; public int ArrSeq { get; set; } public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } }
public class Leg3StopData { public Leg3TripData TripInfo { get; set; } = null!; public string TransferStop2Id { get; set; } = null!; public int DepSeq { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public int StopCount { get; set; } }
public class TwoTransferResult { public LegData Leg1 { get; set; } = null!; public LegData Leg2 { get; set; } = null!; public LegData Leg3 { get; set; } = null!; public int TransferWalk1Meters { get; set; } public int TransferWalk1Seconds { get; set; } public int TransferWalk2Meters { get; set; } public int TransferWalk2Seconds { get; set; } }
