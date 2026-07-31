using System;
using System.Collections.Generic;

namespace TransportDataService.Models.Gtfs.JourneyPlan;

public class JourneyPlanSearchResponse
{
    public FeedMetadataDto? Metadata { get; set; }
    public string ReasonCode { get; set; } = "SUCCESS";
    public List<ItineraryDto> Itineraries { get; set; } = new();
}

public class FeedMetadataDto
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
    public string PlanId { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public int TotalDurationMinutes => (int)(ArrivalTime - DepartureTime).TotalMinutes;
    public int Transfers { get; set; }
    public int TotalWalkingMeters { get; set; }
    public int TotalWalkingMinutes { get; set; }
    public string ServiceDate { get; set; } = null!;
    public int TotalTransitStops { get; set; }
    
    public List<LegDto> Legs { get; set; } = new();
}

public class LegDto
{
    public string Mode { get; set; } = null!; // "WALK" or "TRANSIT"
    
    // TRANSIT ONLY FIELDS
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
    public int StopCount { get; set; }
}
