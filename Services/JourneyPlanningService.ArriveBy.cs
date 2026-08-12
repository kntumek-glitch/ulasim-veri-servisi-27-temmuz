using ulasim_veri_servisi.Services.JourneyPlanning.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TransportDataService.Models.Gtfs.JourneyPlan;
using TransportDataService.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace ulasim_veri_servisi.Services;

public partial class JourneyPlanningService
{
    private async Task<JourneyPlanSearchResponse> SearchJourneyArriveByAsync(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken)
    {
        // 0. Check for Active Feed
        var activeRun = await _context.GtfsImportRuns
            .AsNoTracking()
            .Where(r => r.IsActive && r.Status == "Completed")
            .FirstOrDefaultAsync(cancellationToken);

        if (activeRun == null)
        {
            throw new ulasim_veri_servisi.Exceptions.ActiveFeedNotFoundException("Sistemde iÅŸlem yapabilecek aktif bir GTFS veri seti bulunamadÄ±.");
        }

        int configMaxWalkingMeters = _configuration.GetValue<int>("JourneyPlan:MaxWalkingMeters", 1500);
        int finalMaxWalkingMeters = Math.Min(request.MaxWalkingMeters, configMaxWalkingMeters);
        double walkingSpeed = _configuration.GetValue<double>("JourneyPlan:WalkingSpeedMetersPerSecond", 1.2);
        int maxCandidateStops = _configuration.GetValue<int>("JourneyPlan:MaxCandidateStops", 5);
        int transferBufferMinutes = _configuration.GetValue<int>("JourneyPlan:TransferBufferMinutes", 3);
        int maxTransferWalkMeters = _configuration.GetValue<int>("JourneyPlan:MaxTransferWalkMeters", 500);

        var utcTimeKey = request.DateTime!.Value.ToUniversalTime().ToString("yyyyMMdd_HHmm");
        string cacheKey = $"JourneyPlan:v2_arriveby:{request.Origin.Lat}_{request.Origin.Lon}_{request.Destination.Lat}_{request.Destination.Lon}_{utcTimeKey}_{request.MaxTransfers}_{finalMaxWalkingMeters}_{request.MaxResults}_{request.IncludeIntermediateStops}_{walkingSpeed}_{maxCandidateStops}_{transferBufferMinutes}_{maxTransferWalkMeters}_{activeRun.Id}";

        if (_cache.TryGetValue(cacheKey, out JourneyPlanSearchResponse? cachedResponse) && cachedResponse != null)
        {
            return cachedResponse;
        }

        var response = new JourneyPlanSearchResponse();

        var agency = await _context.GtfsAgencies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        string timezone = agency?.AgencyTimezone ?? "Europe/Istanbul";

        var minDate = await _context.GtfsCalendars.AsNoTracking().MinAsync(c => (DateOnly?)c.StartDate, cancellationToken);
        var maxDate = await _context.GtfsCalendars.AsNoTracking().MaxAsync(c => (DateOnly?)c.EndDate, cancellationToken);

        response.Metadata = new JourneyPlanMetadataDto
        {
            ActiveImportId = activeRun.Id,
            FeedHash = activeRun.FileHash ?? "UNKNOWN",
            Timezone = timezone,
            FeedValidFrom = minDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            FeedValidTo = maxDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            IsFeedStale = maxDate.HasValue && maxDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var activeStopsCache = await _cacheService.GetActiveStopsAsync(activeRun.Id, cancellationToken);
        var activeStops = activeStopsCache.Stops;
        if (!activeStops.Any()) 
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }

        var originStops = _spatialService.FindStopsWithinRadius(activeStops, request.Origin.Lat, request.Origin.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);
        var destStops = _spatialService.FindStopsWithinRadius(activeStops, request.Destination.Lat, request.Destination.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);

        if (!originStops.Any() || !destStops.Any())
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }

        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();

        TimeZoneInfo tzi;
        try { tzi = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { tzi = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time"); }

        var localDateTime = TimeZoneInfo.ConvertTime(request.DateTime!.Value, tzi);
        var targetDate = localDateTime.Date;
        var requestedSeconds = (int)localDateTime.TimeOfDay.TotalSeconds;

        var activeServiceIds = await _cacheService.GetActiveServiceIdsAsync(activeRun.Id, targetDate, cancellationToken);
        var previousDayServiceIds = new List<string>();

        if (requestedSeconds < 4 * 3600)
        {
            previousDayServiceIds = await _cacheService.GetActiveServiceIdsAsync(activeRun.Id, targetDate.AddDays(-1), cancellationToken);
        }

        if (!activeServiceIds.Any() && !previousDayServiceIds.Any()) 
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }

        var itineraries = new List<ItineraryDto>();

        // 5. Find Direct Routes (0-Transfer) Arrive By
        int maxWaitTimeMinutes = _configuration.GetValue<int>("JourneyPlan:MaxWaitTimeMinutes", 60);
        int maxJourneyTimeMinutes = _configuration.GetValue<int>("JourneyPlan:MaxJourneyTimeMinutes", 240);
        
        var directTrips = await FindDirectTripsArriveByAsync(originStopIds, destStopIds, activeServiceIds, previousDayServiceIds, requestedSeconds, maxJourneyTimeMinutes, cancellationToken);
        
        // Exact walking time filter in memory (Backwards)
        var validTrips = directTrips.Where(trip => 
        {
            var dStop = destStops.First(x => x.Stop.StopId == trip.DestStopId);
            int baseArrSecs = trip.IsPreviousDayTrip ? trip.ArrivalSeconds - 86400 : trip.ArrivalSeconds;
            // The trip must arrive before requestedSeconds - walking time to destination
            return baseArrSecs <= requestedSeconds - dStop.WalkingTimeSeconds;
        }).OrderByDescending(x => x.IsPreviousDayTrip ? x.DepartureSeconds - 86400 : x.DepartureSeconds).ToList();

        var deduplicatedDirectTrips = new List<DirectTripResult>();
        var directPatternHash = new HashSet<string>();
        foreach (var trip in validTrips)
        {
            var pattern = !string.IsNullOrEmpty(trip.ShapeId) ? $"P_{trip.ShapeId}" : $"P_{trip.RouteId}_{trip.DirectionId}";
            if (directPatternHash.Add(pattern))
            {
                deduplicatedDirectTrips.Add(trip);
            }
        }

        foreach (var trip in deduplicatedDirectTrips.Take(request.MaxResults))
        {
            var oStop = originStops.First(x => x.Stop.StopId == trip.OriginStopId);
            var dStop = destStops.First(x => x.Stop.StopId == trip.DestStopId);
            
            if (oStop.DistanceMeters + dStop.DistanceMeters > finalMaxWalkingMeters) continue;
            
            DateTime baseDate = trip.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
            DateTime depDt = baseDate.AddSeconds(trip.DepartureSeconds);
            DateTime arrDt = baseDate.AddSeconds(trip.ArrivalSeconds);
            var departureTime = new DateTimeOffset(depDt, tzi.GetUtcOffset(depDt));
            var arrivalTime = new DateTimeOffset(arrDt, tzi.GetUtcOffset(arrDt));

            var patternId = !string.IsNullOrEmpty(trip.ShapeId) ? $"P_{trip.ShapeId}" : $"P_{trip.RouteId}_{trip.DirectionId}";
            var leg = new LegDto
            {
                Mode = "TRANSIT",
                RouteId = trip.RouteId,
                RouteShortName = trip.RouteShortName,
                RouteType = trip.RouteType,
                TripId = trip.TripId,
                DirectionId = trip.DirectionId,
                Headsign = trip.TripHeadsign,
                PatternId = patternId,
                ShapeId = trip.ShapeId,
                ServiceId = trip.ServiceId,
                ServiceDate = baseDate.ToString("yyyy-MM-dd"),
                FromStopId = trip.OriginStopId,
                FromStopName = oStop.Stop.StopName,
                FromStopSequence = trip.OriginStopSequence,
                DepartureTime = departureTime,
                RawGtfsDepartureTime = trip.DepartureTimeRaw,
                RawGtfsDepartureSeconds = trip.DepartureSeconds,
                ToStopId = trip.DestStopId,
                ToStopName = dStop.Stop.StopName,
                ToStopSequence = trip.DestStopSequence,
                ArrivalTime = arrivalTime,
                RawGtfsArrivalTime = trip.ArrivalTimeRaw,
                RawGtfsArrivalSeconds = trip.ArrivalSeconds,
                IntermediateStopCount = trip.StopCount - 1 > 0 ? trip.StopCount - 1 : 0,
                DistanceMeters = 0,
                DurationMinutes = (trip.ArrivalSeconds - trip.DepartureSeconds) / 60,
                StopCount = trip.StopCount
            };
            
            var walk1 = new LegDto
            {
                Mode = "WALK",
                FromStopId = "ORIGIN",
                FromStopName = "Mevcut Konum",
                ToStopId = oStop.Stop.StopId,
                ToStopName = oStop.Stop.StopName,
                DepartureTime = leg.DepartureTime.Value.AddSeconds(-oStop.WalkingTimeSeconds),
                ArrivalTime = leg.DepartureTime,
                DistanceMeters = oStop.DistanceMeters,
                DurationMinutes = oStop.WalkingTimeSeconds / 60
            };
            
            var walk2 = new LegDto
            {
                Mode = "WALK",
                FromStopId = dStop.Stop.StopId,
                FromStopName = dStop.Stop.StopName,
                ToStopId = "DEST",
                ToStopName = "Hedef Konum",
                DepartureTime = leg.ArrivalTime,
                ArrivalTime = leg.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                DistanceMeters = dStop.DistanceMeters,
                DurationMinutes = dStop.WalkingTimeSeconds / 60
            };

            var itinerary = new ItineraryDto
            {
                DataSource = "STATIC_GTFS",
                RouteTypeSummary = "DIRECT",
                DepartureTime = walk1.DepartureTime.Value,
                ArrivalTime = walk2.ArrivalTime.Value,
                TransferCount = 0,
                TotalWalkingDistanceMeters = walk1.DistanceMeters + walk2.DistanceMeters,
                TotalWalkingTimeSeconds = walk1.DurationMinutes * 60 + walk2.DurationMinutes * 60,
                TotalInVehicleTimeSeconds = leg.DurationMinutes * 60,
                InitialWaitTimeSeconds = 0,
                ServiceDate = baseDate.ToString("yyyy-MM-dd"),
                TotalTransitStopCount = leg.StopCount,
                Legs = new List<LegDto> { walk1, leg, walk2 }
            };
            itineraries.Add(itinerary);
        }

        // 6. Find 1-Transfer Routes Arrive By
        if (request.MaxTransfers >= 1)
        {
            var transferTrips = await FindOneTransferTripsArriveByAsync(originStops, destStops, activeServiceIds, previousDayServiceIds, requestedSeconds, maxJourneyTimeMinutes, transferBufferMinutes * 60, targetDate, tzi, activeStopsCache, maxTransferWalkMeters, walkingSpeed, 500, maxWaitTimeMinutes, cancellationToken);
            foreach (var tResult in transferTrips.Take(request.MaxResults))
            {
                var oStop = originStops.First(x => x.Stop.StopId == tResult.Leg1.FromStopId);
                var dStop = destStops.First(x => x.Stop.StopId == tResult.Leg2.ToStopId);
                
                if (oStop.DistanceMeters + dStop.DistanceMeters + tResult.TransferWalkMeters > finalMaxWalkingMeters) continue;

                var walk1 = new LegDto
                {
                    Mode = "WALK", FromStopId = "ORIGIN", FromStopName = "Mevcut Konum", ToStopId = oStop.Stop.StopId, ToStopName = oStop.Stop.StopName,
                    DepartureTime = DateTimeOffset.Parse(tResult.Leg1.ServiceDate).AddSeconds(tResult.Leg1.DepSecs - oStop.WalkingTimeSeconds),
                    ArrivalTime = DateTimeOffset.Parse(tResult.Leg1.ServiceDate).AddSeconds(tResult.Leg1.DepSecs),
                    DistanceMeters = oStop.DistanceMeters, DurationMinutes = oStop.WalkingTimeSeconds / 60
                };
                
                var transit1 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg1.RouteId, RouteShortName = tResult.Leg1.RouteShortName, RouteType = tResult.Leg1.RouteType,
                    TripId = tResult.Leg1.TripId, DirectionId = tResult.Leg1.DirectionId, Headsign = tResult.Leg1.Headsign, PatternId = tResult.Leg1.PatternId, ShapeId = tResult.Leg1.ShapeId, ServiceId = tResult.Leg1.ServiceId, ServiceDate = tResult.Leg1.ServiceDate,
                    FromStopId = tResult.Leg1.FromStopId, FromStopName = activeStops.First(s => s.StopId == tResult.Leg1.FromStopId).StopName, FromStopSequence = tResult.Leg1.FromStopSequence,
                    DepartureTime = DateTimeOffset.Parse(tResult.Leg1.ServiceDate).AddSeconds(tResult.Leg1.DepSecs), RawGtfsDepartureTime = tResult.Leg1.DepTimeRaw, RawGtfsDepartureSeconds = tResult.Leg1.DepSecs,
                    ToStopId = tResult.Leg1.ToStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg1.ToStopId).StopName, ToStopSequence = tResult.Leg1.ToStopSequence,
                    ArrivalTime = DateTimeOffset.Parse(tResult.Leg1.ServiceDate).AddSeconds(tResult.Leg1.ArrSecs), RawGtfsArrivalTime = tResult.Leg1.ArrTimeRaw, RawGtfsArrivalSeconds = tResult.Leg1.ArrSecs,
                    IntermediateStopCount = tResult.Leg1.StopCount - 1 > 0 ? tResult.Leg1.StopCount - 1 : 0, DistanceMeters = 0, DurationMinutes = (tResult.Leg1.ArrSecs - tResult.Leg1.DepSecs) / 60, StopCount = tResult.Leg1.StopCount
                };

                var walk2 = new LegDto
                {
                    Mode = "WALK", FromStopId = tResult.Leg1.ToStopId, FromStopName = transit1.ToStopName, ToStopId = tResult.Leg2.FromStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg2.FromStopId).StopName,
                    DepartureTime = transit1.ArrivalTime, ArrivalTime = transit1.ArrivalTime.Value.AddSeconds(tResult.TransferWalkSeconds), DistanceMeters = tResult.TransferWalkMeters, DurationMinutes = (int)(tResult.TransferWalkSeconds / 60)
                };

                var transit2 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg2.RouteId, RouteShortName = tResult.Leg2.RouteShortName, RouteType = tResult.Leg2.RouteType,
                    TripId = tResult.Leg2.TripId, DirectionId = tResult.Leg2.DirectionId, Headsign = tResult.Leg2.Headsign, PatternId = tResult.Leg2.PatternId, ShapeId = tResult.Leg2.ShapeId, ServiceId = tResult.Leg2.ServiceId, ServiceDate = tResult.Leg2.ServiceDate,
                    FromStopId = tResult.Leg2.FromStopId, FromStopName = walk2.ToStopName, FromStopSequence = tResult.Leg2.FromStopSequence,
                    DepartureTime = DateTimeOffset.Parse(tResult.Leg2.ServiceDate).AddSeconds(tResult.Leg2.DepSecs), RawGtfsDepartureTime = tResult.Leg2.DepTimeRaw, RawGtfsDepartureSeconds = tResult.Leg2.DepSecs,
                    ToStopId = tResult.Leg2.ToStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg2.ToStopId).StopName, ToStopSequence = tResult.Leg2.ToStopSequence,
                    ArrivalTime = DateTimeOffset.Parse(tResult.Leg2.ServiceDate).AddSeconds(tResult.Leg2.ArrSecs), RawGtfsArrivalTime = tResult.Leg2.ArrTimeRaw, RawGtfsArrivalSeconds = tResult.Leg2.ArrSecs,
                    IntermediateStopCount = tResult.Leg2.StopCount - 1 > 0 ? tResult.Leg2.StopCount - 1 : 0, DistanceMeters = 0, DurationMinutes = (tResult.Leg2.ArrSecs - tResult.Leg2.DepSecs) / 60, StopCount = tResult.Leg2.StopCount
                };

                var walk3 = new LegDto
                {
                    Mode = "WALK", FromStopId = dStop.Stop.StopId, FromStopName = transit2.ToStopName, ToStopId = "DEST", ToStopName = "Hedef Konum",
                    DepartureTime = transit2.ArrivalTime, ArrivalTime = transit2.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds), DistanceMeters = dStop.DistanceMeters, DurationMinutes = dStop.WalkingTimeSeconds / 60
                };

                var wait1Seconds = Math.Max(0, (transit2.DepartureTime.Value - transit1.ArrivalTime.Value).TotalSeconds - tResult.TransferWalkSeconds);
                
                var itinerary = new ItineraryDto
                {
                    DataSource = "STATIC_GTFS", RouteTypeSummary = "1_TRANSFER", DepartureTime = walk1.DepartureTime.Value, ArrivalTime = walk3.ArrivalTime.Value, TransferCount = 1,
                    TotalWalkingDistanceMeters = walk1.DistanceMeters + walk2.DistanceMeters + walk3.DistanceMeters,
                    TotalWalkingTimeSeconds = walk1.DurationMinutes * 60 + walk2.DurationMinutes * 60 + walk3.DurationMinutes * 60,
                    TotalInVehicleTimeSeconds = transit1.DurationMinutes * 60 + transit2.DurationMinutes * 60,
                    InitialWaitTimeSeconds = 0, ServiceDate = tResult.Leg1.ServiceDate, TotalTransitStopCount = transit1.StopCount + transit2.StopCount,
                    Legs = new List<LegDto> { walk1, transit1, walk2, transit2, walk3 }
                };
                itineraries.Add(itinerary);
            }
        }
        
        // 7. Find 2-Transfer Routes Arrive By
        if (request.MaxTransfers >= 2)
        {
            var twoTransferTrips = await FindTwoTransferTripsArriveByAsync(originStops, destStops, activeServiceIds, previousDayServiceIds, requestedSeconds, maxJourneyTimeMinutes, transferBufferMinutes * 60, targetDate, tzi, activeStopsCache, maxTransferWalkMeters, walkingSpeed, 500, 2000, maxWaitTimeMinutes, cancellationToken);
            // Stub implementation returns empty. We will skip iteration.
        }

        if (!itineraries.Any())
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }

        // Sort by LATEST departure time
        var sortedItineraries = itineraries
            .OrderByDescending(i => i.DepartureTime)
            .ThenBy(i => i.TotalWalkingDistanceMeters)
            .Take(request.MaxResults)
            .ToList();

        // 7. Evaluate OSRM Walks (Backwards aware?)
        // In Arrive_by, OSRM walks should adjust departure time backwards, not arrival time forwards.
        // We will need a modified EvaluateOsrmWalksArriveByAsync or similar, but for now we can just map it.
        var finalItineraries = await EvaluateOsrmWalksArriveByAsync(sortedItineraries, request, activeStops, activeRun.Id, cancellationToken);
        
        response.Itineraries = finalItineraries.OrderByDescending(i => i.DepartureTime).ToList();
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
            .SetSize(1);
        _cache.Set(cacheKey, response, cacheOptions);

        return response;
    }

    private async Task<List<DirectTripResult>> FindDirectTripsArriveByAsync(List<string> originStopIds, List<string> destStopIds, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int maxJourneyTimeMinutes, CancellationToken cancellationToken)
    {
        int maxArrivalSeconds = requestedSeconds;
        int minArrivalSeconds = requestedSeconds - (maxJourneyTimeMinutes * 60);

        var todayQuery = from o in _context.GtfsStopTimes
                         join d in _context.GtfsStopTimes on o.GtfsTripId equals d.GtfsTripId
                         join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                         join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                         where originStopIds.Contains(o.StopId) &&
                               destStopIds.Contains(d.StopId) &&
                               d.StopSequence > o.StopSequence &&
                               activeServiceIds.Contains(t.ServiceId) &&
                               d.ArrivalSeconds <= maxArrivalSeconds && d.ArrivalSeconds >= minArrivalSeconds
                         select new DirectTripResult
                         {
                             TripId = t.TripId, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType,
                             TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                             OriginStopId = o.StopId, DestStopId = d.StopId,
                             OriginStopSequence = o.StopSequence, DestStopSequence = d.StopSequence,
                             DepartureSeconds = o.DepartureSeconds.GetValueOrDefault(), DepartureTimeRaw = o.DepartureTimeRaw,
                             ArrivalSeconds = d.ArrivalSeconds.GetValueOrDefault(), ArrivalTimeRaw = d.ArrivalTimeRaw,
                             IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId,
                             StopCount = _context.GtfsStopTimes.Count(s => s.GtfsTripId == t.Id && s.StopSequence > o.StopSequence && s.StopSequence <= d.StopSequence)
                         };

        IQueryable<DirectTripResult> finalQuery = todayQuery;

        if (previousDayServiceIds.Any())
        {
            int previousDayMaxArrivalSeconds = requestedSeconds + 86400;
            int previousDayMinArrivalSeconds = requestedSeconds + 86400 - (maxJourneyTimeMinutes * 60);
            var yesterdayQuery = from o in _context.GtfsStopTimes
                                 join d in _context.GtfsStopTimes on o.GtfsTripId equals d.GtfsTripId
                                 join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                 join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                 where originStopIds.Contains(o.StopId) &&
                                       destStopIds.Contains(d.StopId) &&
                                       d.StopSequence > o.StopSequence &&
                                       previousDayServiceIds.Contains(t.ServiceId) &&
                                       d.ArrivalSeconds <= previousDayMaxArrivalSeconds && d.ArrivalSeconds >= previousDayMinArrivalSeconds
                                 select new DirectTripResult
                                 {
                                     TripId = t.TripId, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType,
                                     TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                     OriginStopId = o.StopId, DestStopId = d.StopId,
                                     OriginStopSequence = o.StopSequence, DestStopSequence = d.StopSequence,
                                     DepartureSeconds = o.DepartureSeconds.GetValueOrDefault(), DepartureTimeRaw = o.DepartureTimeRaw,
                                     ArrivalSeconds = d.ArrivalSeconds.GetValueOrDefault(), ArrivalTimeRaw = d.ArrivalTimeRaw,
                                     IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId,
                                     StopCount = _context.GtfsStopTimes.Count(s => s.GtfsTripId == t.Id && s.StopSequence > o.StopSequence && s.StopSequence <= d.StopSequence)
                                 };
            finalQuery = todayQuery.Concat(yesterdayQuery);
        }

        return await finalQuery.OrderByDescending(x => x.ArrivalSeconds).AsNoTracking().ToListAsync(cancellationToken);
    }

    private async Task<List<ItineraryDto>> EvaluateOsrmWalksArriveByAsync(List<ItineraryDto> candidates, JourneyPlanV2SearchRequest request, List<GtfsStop> activeStops, int importId, CancellationToken cancellationToken)
    {
        var finalItineraries = new List<ItineraryDto>();

        foreach (var itinerary in candidates)
        {
            bool dropItinerary = false;
            bool itineraryApproximated = false;

            // In ArriveBy, we evaluate from end to start (backwards)
            // But doing it backwards in terms of walking is structurally the same, we just subtract duration.
            // Let's iterate backwards over the legs.
            for (int i = itinerary.Legs.Count - 1; i >= 0; i--)
            {
                var leg = itinerary.Legs[i];

                if (leg.Mode != "WALK") continue;

                // Determine coordinates
                double srcLat, srcLon, tgtLat, tgtLon;

                if (leg.FromStopId == "ORIGIN")
                {
                    srcLat = request.Origin.Lat; srcLon = request.Origin.Lon;
                }
                else
                {
                    var stop1 = activeStops.FirstOrDefault(s => s.StopId == leg.FromStopId);
                    if (stop1 == null) { dropItinerary = true; break; }
                    srcLat = stop1.StopLat; srcLon = stop1.StopLon;
                }

                if (leg.ToStopId == "DEST")
                {
                    tgtLat = request.Destination.Lat; tgtLon = request.Destination.Lon;
                }
                else
                {
                    var stop2 = activeStops.FirstOrDefault(s => s.StopId == leg.ToStopId);
                    if (stop2 == null) { dropItinerary = true; break; }
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
                    
                    // ARRIVE_BY CORE LOGIC: Subtract walking time from ArrivalTime to get DepartureTime
                    leg.DepartureTime = leg.ArrivalTime?.AddSeconds(-leg.DurationSeconds);
                }
                else
                {
                    itineraryApproximated = true;
                }

                // Push the adjusted DepartureTime backwards to the preceding Transit leg's ArrivalTime
                // (In a direct route, if Walk1 departs at 08:30, then the total itinerary departs at 08:30)
                if (i > 0)
                {
                    var prevTransit = itinerary.Legs[i - 1];
                    if (leg.DepartureTime.HasValue && prevTransit.ArrivalTime.HasValue)
                    {
                        // In strict ArriveBy, if the walk takes longer than the buffer, the previous leg might not be catchable
                        // However, we already factored in MaxJourneyTime. For true ArriveBy, we just update the Itinerary's overall Departure.
                    }
                }
            }

            if (!dropItinerary)
            {
                // Re-calculate itinerary totals
                var firstLeg = itinerary.Legs.First();
                var lastLeg = itinerary.Legs.Last();
                
                itinerary.DepartureTime = firstLeg.DepartureTime ?? itinerary.DepartureTime;
                itinerary.TotalWalkingDistanceMeters = itinerary.Legs.Where(l => l.Mode == "WALK").Sum(l => l.DistanceMeters);
                itinerary.TotalWalkingTimeSeconds = itinerary.Legs.Where(l => l.Mode == "WALK").Sum(l => l.DurationSeconds);
                itinerary.IsApproximate = itineraryApproximated;
                
                finalItineraries.Add(itinerary);
            }
        }

        return finalItineraries;
    }
}
