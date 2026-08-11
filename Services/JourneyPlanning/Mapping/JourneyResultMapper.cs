using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services.JourneyPlanning.Mapping;

public class JourneyResultMapper : IJourneyResultMapper
{
    private readonly AppDbContext _context;
    private readonly WalkingRoutingService _walkingRoutingService;
    private readonly IConfiguration _configuration;

    public JourneyResultMapper(AppDbContext context, WalkingRoutingService walkingRoutingService, IConfiguration configuration)
    {
        _context = context;
        _walkingRoutingService = walkingRoutingService;
        _configuration = configuration;
    }

    public ItineraryDto CreateItineraryDto(JourneyPlanSearchRequest request, List<LegDto> legs, string serviceDate)
    {
        var transitLegs = legs.Where(l => l.Mode == "TRANSIT").ToList();
        var walkLegs = legs.Where(l => l.Mode == "WALK").ToList();

        var departureTime = legs.First().DepartureTime!.Value;
        var arrivalTime = legs.Last().ArrivalTime!.Value;

        var initialWait = (int)(departureTime - request.DepartureDateTime).GetValueOrDefault().TotalSeconds;
        if (initialWait < 0) initialWait = 0;

        var transferWaitTimes = new List<int>();
        for (int i = 0; i < transitLegs.Count - 1; i++)
        {
            var currentTransit = transitLegs[i];
            var nextTransit = transitLegs[i + 1];
            
            var walkBetween = legs.FirstOrDefault(l => l.Mode == "WALK" && l.DepartureTime == currentTransit.ArrivalTime);
            var arrivedAtNextStop = walkBetween != null ? walkBetween.ArrivalTime!.Value : currentTransit.ArrivalTime!.Value;
            
            var wait = (int)(nextTransit.DepartureTime!.Value - arrivedAtNextStop).TotalSeconds;
            transferWaitTimes.Add(wait < 0 ? 0 : wait);
        }

        var totalWait = initialWait + transferWaitTimes.Sum();
        var totalWalkDistance = walkLegs.Sum(l => l.DistanceMeters);
        var totalWalkTime = walkLegs.Sum(l => (int)(l.ArrivalTime!.Value - l.DepartureTime!.Value).TotalSeconds);
        var totalInVehicleTime = transitLegs.Sum(l => (int)(l.ArrivalTime!.Value - l.DepartureTime!.Value).TotalSeconds);
        var totalTransitStops = transitLegs.Sum(l => l.StopCount);

        var routeTypes = transitLegs.Where(l => l.RouteType.HasValue).Select(l => l.RouteType!.Value).Distinct().Select(rt => 
        {
            return rt switch
            {
                0 => "Tram",
                1 => "Subway",
                2 => "Rail",
                3 => "Bus",
                4 => "Ferry",
                5 => "Cable Tram",
                6 => "Aerial Lift",
                7 => "Funicular",
                11 => "Trolleybus",
                12 => "Monorail",
                _ => "Transit"
            };
        }).ToList();
        var routeTypeSummary = routeTypes.Any() ? string.Join(" + ", routeTypes) : "Walk";

        return new ItineraryDto
        {
            PlanId = Guid.NewGuid().ToString(),
            DataSource = "STATIC_GTFS",
            DepartureTime = departureTime,
            ArrivalTime = arrivalTime,
            ServiceDate = serviceDate,
            TransferCount = transitLegs.Count > 0 ? transitLegs.Count - 1 : 0,
            TotalWalkingDistanceMeters = totalWalkDistance,
            TotalWalkingTimeSeconds = totalWalkTime,
            TotalWaitingTimeSeconds = totalWait,
            TotalInVehicleTimeSeconds = totalInVehicleTime,
            InitialWaitTimeSeconds = initialWait,
            TransferWaitTimes = transferWaitTimes,
            TotalTransitStopCount = totalTransitStops,
            RouteTypeSummary = routeTypeSummary,
            Legs = legs
        };
    }

    public async Task PopulateIntermediateStopsAsync(List<ItineraryDto> itineraries, TimeZoneInfo tzi, int importId, CancellationToken cancellationToken)
    {
        var transitLegs = itineraries
            .SelectMany(i => i.Legs)
            .Where(l => l.Mode == "TRANSIT")
            .ToList();

        if (!transitLegs.Any()) return;

        var tripIds = transitLegs.Select(l => l.TripId).Where(t => t != null).Distinct().ToList();

        var stopTimes = await _context.GtfsStopTimes
            .Include(st => st.Stop)
            .Where(st => st.GtfsImportRunId == importId && tripIds.Contains(st.TripId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var leg in transitLegs)
        {
            if (leg.TripId == null || leg.FromStopSequence == null || leg.ToStopSequence == null || leg.ServiceDate == null) 
                continue;

            var baseDate = DateTime.Parse(leg.ServiceDate);
            var intermediates = stopTimes
                .Where(st => st.TripId == leg.TripId && st.StopSequence > leg.FromStopSequence.Value && st.StopSequence < leg.ToStopSequence.Value)
                .OrderBy(st => st.StopSequence)
                .Select(st => 
                {
                    DateTime stDepDt = baseDate.AddSeconds(st.DepartureSeconds ?? 0);
                    DateTime stArrDt = baseDate.AddSeconds(st.ArrivalSeconds ?? 0);
                    return new IntermediateStopDto
                    {
                        StopId = st.StopId,
                        StopName = st.Stop?.StopName ?? "Unknown",
                        StopSequence = st.StopSequence,
                        ArrivalTime = new DateTimeOffset(stArrDt, tzi.GetUtcOffset(stArrDt)),
                        ArrivalSeconds = st.ArrivalSeconds,
                        DepartureSeconds = st.DepartureSeconds,
                        RawGtfsArrivalTime = st.ArrivalTimeRaw,
                        RawGtfsDepartureTime = st.DepartureTimeRaw,
                        Lat = st.Stop?.StopLat ?? 0,
                        Lon = st.Stop?.StopLon ?? 0
                    };
                }).ToList();
            
            leg.IntermediateStops = intermediates;
        }
    }

    private void UpdateItineraryMetrics(ItineraryDto itinerary, JourneyPlanSearchRequest request)
    {
        var transitLegs = itinerary.Legs.Where(l => l.Mode == "TRANSIT").ToList();
        var walkLegs = itinerary.Legs.Where(l => l.Mode == "WALK").ToList();

        itinerary.DepartureTime = itinerary.Legs.First().DepartureTime.GetValueOrDefault();
        itinerary.ArrivalTime = itinerary.Legs.Last().ArrivalTime.GetValueOrDefault();

        var initialWait = (int)(itinerary.DepartureTime - request.DepartureDateTime).GetValueOrDefault().TotalSeconds;
        if (initialWait < 0) initialWait = 0;

        var transferWaitTimes = new List<int>();
        for (int i = 0; i < transitLegs.Count - 1; i++)
        {
            var currentTransit = transitLegs[i];
            var nextTransit = transitLegs[i + 1];
            
            var walkBetween = itinerary.Legs.FirstOrDefault(l => l.Mode == "WALK" && l.DepartureTime == currentTransit.ArrivalTime);
            var arrivedAtNextStop = walkBetween != null ? walkBetween.ArrivalTime!.Value : currentTransit.ArrivalTime!.Value;
            
            var wait = (int)(nextTransit.DepartureTime!.Value - arrivedAtNextStop).TotalSeconds;
            transferWaitTimes.Add(wait < 0 ? 0 : wait);
        }

        itinerary.TotalWalkingDistanceMeters = walkLegs.Sum(l => l.DistanceMeters);
        itinerary.TotalWalkingTimeSeconds = walkLegs.Sum(l => (int)(l.ArrivalTime!.Value - l.DepartureTime!.Value).TotalSeconds);
        itinerary.TotalInVehicleTimeSeconds = transitLegs.Sum(l => (int)(l.ArrivalTime!.Value - l.DepartureTime!.Value).TotalSeconds);
        itinerary.InitialWaitTimeSeconds = initialWait;
        itinerary.TransferWaitTimes = transferWaitTimes;
        itinerary.TotalWaitingTimeSeconds = initialWait + transferWaitTimes.Sum();
    }

    private async Task<ItineraryDto?> FindNextTripForPatternAsync(ItineraryDto itinerary, int missedLegIndex, DateTimeOffset newReadyTime, int importId, CancellationToken cancellationToken)
    {
        var missedLeg = itinerary.Legs[missedLegIndex];
        if (missedLeg.PatternId == null || missedLeg.FromStopSequence == null) return null;

        int searchSeconds = (int)newReadyTime.TimeOfDay.TotalSeconds;
        if (newReadyTime.Date < newReadyTime.ToLocalTime().Date) 
        {
            // Cross-day unhandled for now
        }

        var routeId = missedLeg.RouteId;
        var dirId = missedLeg.DirectionId;
        if (routeId == null) return null;

        var nextStopTime = await _context.GtfsStopTimes
            .Include(st => st.Trip)
            .Where(st => st.GtfsImportRunId == importId
                      && st.StopId == missedLeg.FromStopId
                      && st.Trip.RouteId == routeId
                      && st.Trip.DirectionId == dirId
                      && st.DepartureSeconds >= searchSeconds)
            .OrderBy(st => st.DepartureSeconds)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextStopTime == null || !nextStopTime.DepartureSeconds.HasValue) return null;

        var shiftSeconds = nextStopTime.DepartureSeconds.Value - missedLeg.RawGtfsDepartureSeconds.GetValueOrDefault();
        
        missedLeg.TripId = nextStopTime.TripId;
        missedLeg.DepartureTime = missedLeg.DepartureTime?.AddSeconds(shiftSeconds);
        if (missedLeg.ArrivalTime.HasValue) missedLeg.ArrivalTime = missedLeg.ArrivalTime.Value.AddSeconds(shiftSeconds);
        missedLeg.RawGtfsDepartureSeconds = nextStopTime.DepartureSeconds;
        missedLeg.RawGtfsArrivalSeconds += shiftSeconds; 

        for (int i = missedLegIndex + 1; i < itinerary.Legs.Count; i++)
        {
            var leg = itinerary.Legs[i];
            leg.DepartureTime = leg.DepartureTime?.AddSeconds(shiftSeconds);
            if (leg.ArrivalTime.HasValue) leg.ArrivalTime = leg.ArrivalTime.Value.AddSeconds(shiftSeconds);
            if (leg.RawGtfsDepartureSeconds.HasValue) leg.RawGtfsDepartureSeconds += shiftSeconds;
            if (leg.RawGtfsArrivalSeconds.HasValue) leg.RawGtfsArrivalSeconds += shiftSeconds;
        }

        return itinerary;
    }

    public async Task<List<ItineraryDto>> EvaluateOsrmWalksAsync(List<ItineraryDto> candidates, JourneyPlanSearchRequest request, List<GtfsStop> activeStops, int importId, CancellationToken cancellationToken)
    {
        var validated = new List<ItineraryDto>();

        foreach (var itinerary in candidates)
        {
            bool dropItinerary = false;
            bool itineraryApproximated = false;

            for (int i = 0; i < itinerary.Legs.Count; i++)
            {
                var leg = itinerary.Legs[i];
                if (leg.Mode != "WALK") continue;

                double srcLat, srcLon, tgtLat, tgtLon;

                if (i == 0) // Origin to first stop
                {
                    srcLat = request.Origin.Lat;
                    srcLon = request.Origin.Lon;
                    var stop = activeStops.FirstOrDefault(s => s.StopId == leg.ToStopId);
                    if (stop == null) { dropItinerary = true; break; }
                    tgtLat = stop.StopLat; tgtLon = stop.StopLon;
                }
                else if (i == itinerary.Legs.Count - 1) // Last stop to destination
                {
                    var stop = activeStops.FirstOrDefault(s => s.StopId == leg.FromStopId);
                    if (stop == null) { dropItinerary = true; break; }
                    srcLat = stop.StopLat; srcLon = stop.StopLon;
                    tgtLat = request.Destination.Lat; tgtLon = request.Destination.Lon;
                }
                else // Transfer walk
                {
                    var stop1 = activeStops.FirstOrDefault(s => s.StopId == leg.FromStopId);
                    var stop2 = activeStops.FirstOrDefault(s => s.StopId == leg.ToStopId);
                    if (stop1 == null || stop2 == null) { dropItinerary = true; break; }
                    srcLat = stop1.StopLat; srcLon = stop1.StopLon;
                    tgtLat = stop2.StopLat; tgtLon = stop2.StopLon;
                }

                var osrmResult = await _walkingRoutingService.CalculateWalkingRouteAsync(srcLat, srcLon, tgtLat, tgtLon, request.IncludeWalkingGeometry, cancellationToken);
                
                if (osrmResult.State.ErrorCode == "UNROUTABLE_LOCATION" || osrmResult.State.ErrorCode == "NO_ROUTE")
                {
                    dropItinerary = true;
                    break;
                }
                
                if (osrmResult.State.IsSuccess)
                {
                    leg.DistanceMeters = (int)osrmResult.DistanceMeters;
                    leg.DurationSeconds = (int)osrmResult.DurationSeconds;
                    leg.DurationMinutes = leg.DurationSeconds / 60;
                    leg.GeometryGeoJson = osrmResult.GeometryGeoJson;
                    
                    leg.ArrivalTime = leg.DepartureTime?.AddSeconds(leg.DurationSeconds);
                }
                else
                {
                    itineraryApproximated = true;
                }

                if (i + 1 < itinerary.Legs.Count)
                {
                    var nextTransit = itinerary.Legs[i + 1];
                    
                    if (!leg.ArrivalTime.HasValue) 
                    {
                        dropItinerary = true;
                        break;
                    }

                    int currentBufferSeconds = (i > 0) ? _configuration.GetValue<int>("JourneyPlan:TransferBufferMinutes", 3) * 60 : 0;
                    var earliestCatchableTime = leg.ArrivalTime.Value.AddSeconds(currentBufferSeconds);
                    
                    if (earliestCatchableTime > nextTransit.DepartureTime)
                    {
                        var newItinerary = await FindNextTripForPatternAsync(itinerary, i + 1, earliestCatchableTime, importId, cancellationToken);
                        if (newItinerary == null)
                        {
                            dropItinerary = true;
                            break; 
                        }
                    }
                }
            }

            if (!dropItinerary)
            {
                itinerary.IsApproximate = itineraryApproximated;
                UpdateItineraryMetrics(itinerary, request);
                validated.Add(itinerary);
            }
        }

        return validated;
    }
}
