using System;
using System.Collections.Generic;

namespace TransportDataService.Models.Gtfs.JourneyPlan;

public enum JourneyPlanResolutionCode
{
    SUCCESS,
    NO_NEARBY_ORIGIN_STOP,
    NO_NEARBY_DESTINATION_STOP,
    NO_ACTIVE_SERVICE,
    NO_ROUTE_FOUND,
    WALKING_ROUTE_UNAVAILABLE,
    SEARCH_TIMEOUT,
    CLIENT_CANCELLED,
    FEED_NOT_AVAILABLE,
    FEED_STALE,
    INTERNAL_ERROR
}

public class JourneyPlanSearchResponse
{
    public JourneyPlanMetadataDto? Metadata { get; set; }
    public string ReasonCode { get; set; } = JourneyPlanResolutionCode.SUCCESS.ToString();
    public List<ItineraryDto> Itineraries { get; set; } = new();
}

public class JourneyPlanMetadataDto
{
    public int ActiveImportId { get; set; }
    public string FeedHash { get; set; } = null!;
    public string StartDate { get; set; } = null!;
    public string EndDate { get; set; } = null!;
    public bool IsStale { get; set; }
    public string Timezone { get; set; } = null!;
    public string DataSourceWarning { get; set; } = "Sonuçlar statik (planlı) tarife verisine dayanmaktadır, canlı araç konumu/trafiği içermez.";
}

public class ItineraryDto
{
    public string PlanId { get; set; } = string.Empty;
    public string DataSource { get; set; } = "STATIC_GTFS";
    public string RouteTypeSummary { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public int TotalDurationMinutes => (int)(ArrivalTime - DepartureTime).TotalMinutes;
    public int TotalJourneyTimeSeconds => (int)(ArrivalTime - DepartureTime).TotalSeconds;
    public int TransferCount { get; set; }
    public int TotalWalkingDistanceMeters { get; set; }
    public int TotalWalkingTimeSeconds { get; set; }
    public int TotalWaitingTimeSeconds { get; set; }
    public int TotalInVehicleTimeSeconds { get; set; }
    public int InitialWaitTimeSeconds { get; set; }
    public List<int> TransferWaitTimes { get; set; } = new();
    public string ServiceDate { get; set; } = null!;
    public int TotalTransitStopCount { get; set; }
    public bool IsApproximate { get; set; }
    
    public List<LegDto> Legs { get; set; } = new();
}

public class LegDto
{
    public string Mode { get; set; } = null!; // "WALK" or "TRANSIT"
    
    // TRANSIT ONLY FIELDS
    public int? RouteType { get; set; }
    public string? PatternId { get; set; }
    public string? ShapeId { get; set; }
    public string? RouteId { get; set; }
    public string? RouteShortName { get; set; }
    public string? TripId { get; set; }
    public int? DirectionId { get; set; }
    public string? Headsign { get; set; }
    public string? ServiceId { get; set; }
    public string? ServiceDate { get; set; }
    
    // STOP IDENTIFIERS (BOTH WALK AND TRANSIT)
    public string? FromStopId { get; set; }
    public string? FromStopName { get; set; }
    public int? FromStopSequence { get; set; }
    
    public string? ToStopId { get; set; }
    public string? ToStopName { get; set; }
    public int? ToStopSequence { get; set; }
    
    // TRANSIT TIMES
    public string? RawGtfsDepartureTime { get; set; }
    public int? RawGtfsDepartureSeconds { get; set; }
    public DateTimeOffset? DepartureTime { get; set; }
    
    public string? RawGtfsArrivalTime { get; set; }
    public int? RawGtfsArrivalSeconds { get; set; }
    public DateTimeOffset? ArrivalTime { get; set; }
    
    public int IntermediateStopCount { get; set; }
    
    // METRICS
    public int DistanceMeters { get; set; }
    public int DurationMinutes { get; set; }
    public int DurationSeconds { get; set; }
    public int StopCount { get; set; }
    public object? GeometryGeoJson { get; set; }
    
    // WALKING METADATA
    public string? WalkingSource { get; set; }
    public bool IsApproximate { get; set; }
    public string? WalkingWarning { get; set; }
    public bool HasGeometry { get; set; }

    // INTERMEDIATE STOPS
    public List<IntermediateStopDto>? IntermediateStops { get; set; }
}

public class IntermediateStopDto
{
    public string StopId { get; set; } = null!;
    public string? StopCode { get; set; }
    public string StopName { get; set; } = null!;
    public int StopSequence { get; set; }
    public string? RawGtfsArrivalTime { get; set; }
    public string? RawGtfsDepartureTime { get; set; }
    public int? ArrivalSeconds { get; set; }
    public int? DepartureSeconds { get; set; }
    public DateTimeOffset? ArrivalTime { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
}
