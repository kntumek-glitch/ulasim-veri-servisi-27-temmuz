using System;
using System.Collections.Generic;

namespace ulasim_veri_servisi.Models.Routing;

public class RoutingSnapshot
{
    // Snapshot Versioning & Metadata
    public int ActiveImportId { get; set; }
    public string FeedHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string AlgorithmVersion { get; set; } = "1.0";

    // O(1) / O(log N) Indexed Data Structures
    public Dictionary<string, List<string>> StopToPatterns { get; set; } = new();
    public Dictionary<string, List<string>> PatternToStops { get; set; } = new();
    public Dictionary<string, List<string>> PatternToTrips { get; set; } = new();
    public Dictionary<string, string> TripToServiceId { get; set; } = new();
    public Dictionary<string, List<SnapshotStopTime>> TripTimetables { get; set; } = new();
    public Dictionary<string, List<int>> PatternStopDepartures { get; set; } = new();
    public Dictionary<string, List<SnapshotTransfer>> StopTransfers { get; set; } = new();
    public Dictionary<string, List<SnapshotTransfer>> StopTransfersReverse { get; set; } = new();
    public Dictionary<string, PatternMetadata> PatternMetadata { get; set; } = new();
    public Dictionary<string, SnapshotCalendar> ServiceCalendars { get; set; } = new();
    public Dictionary<string, SnapshotStop> Stops { get; set; } = new();

    // RAPTOR Array Optimizations
    public Dictionary<string, int> StopIdToIndex { get; set; } = new();
    public SnapshotStop[] StopsByIndex { get; set; } = Array.Empty<SnapshotStop>();
}

public class SnapshotStopTime
{
    public string StopId { get; set; } = string.Empty;
    public int StopSequence { get; set; }
    public int ArrivalSeconds { get; set; }
    public int DepartureSeconds { get; set; }
    public string DepartureTimeRaw { get; set; } = string.Empty;
    public string ArrivalTimeRaw { get; set; } = string.Empty;
}

public class SnapshotTransfer
{
    public string FromStopId { get; set; } = string.Empty;
    public string ToStopId { get; set; } = string.Empty;
    public int DistanceMeters { get; set; }
    public int WalkingTimeSeconds { get; set; }
}

public class PatternMetadata
{
    public string PatternId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string RouteShortName { get; set; } = string.Empty;
    public int? RouteType { get; set; }
    public string? ShapeId { get; set; }
    public int? DirectionId { get; set; }
    public string? Headsign { get; set; }
}

public class SnapshotCalendar
{
    public string ServiceId { get; set; } = string.Empty;
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public HashSet<string> AddedDates { get; set; } = new();
    public HashSet<string> RemovedDates { get; set; } = new();
}

public class SnapshotStop
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public double StopLat { get; set; }
    public double StopLon { get; set; }
}
