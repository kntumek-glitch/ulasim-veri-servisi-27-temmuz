using ulasim_veri_servisi.Services.JourneyPlanning.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services;

public partial class JourneyPlanningService
{
    private async Task<List<OneTransferResult>> FindOneTransferTripsArriveByAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int maxJourneyTimeMinutes, int transferBufferSeconds, DateTime targetDate, TimeZoneInfo tzi, ActiveStopsCache activeStopsCache, int maxTransferWalkMeters, double walkingSpeed, int maxLegTrips, int maxWaitTimeMinutes, CancellationToken cancellationToken)
    {
        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();
        
        int maxArrivalSeconds = requestedSeconds;
        int minArrivalSeconds = requestedSeconds - (maxWaitTimeMinutes * 60) - (maxJourneyTimeMinutes * 60);
        
        // 1. BACKWARD: Find Leg 2 trips arriving at destination before requestedSeconds
        var todayLeg2Query = from d in _context.GtfsStopTimes
                               join t in _context.GtfsTrips on d.GtfsTripId equals t.Id
                               join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                               where destStopIds.Contains(d.StopId) &&
                                     activeServiceIds.Contains(t.ServiceId) &&
                                     d.ArrivalSeconds <= maxArrivalSeconds && d.ArrivalSeconds >= minArrivalSeconds
                                   select new Leg2TripData {
                                       TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                       DestStopId = d.StopId, ArrSeq = d.StopSequence, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                   };

        IQueryable<Leg2TripData> finalLeg2Query = todayLeg2Query;

        if (previousDayServiceIds.Any())
        {
            int previousDayMaxArrivalSeconds = requestedSeconds + 86400;
            int previousDayMinArrivalSeconds = requestedSeconds + 86400 - (maxWaitTimeMinutes * 60) - (maxJourneyTimeMinutes * 60);
            var yesterdayLeg2Query = from d in _context.GtfsStopTimes
                                       join t in _context.GtfsTrips on d.GtfsTripId equals t.Id
                                       join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                       where destStopIds.Contains(d.StopId) &&
                                             previousDayServiceIds.Contains(t.ServiceId) &&
                                             d.ArrivalSeconds <= previousDayMaxArrivalSeconds && d.ArrivalSeconds >= previousDayMinArrivalSeconds
                                       select new Leg2TripData {
                                           TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                           DestStopId = d.StopId, ArrSeq = d.StopSequence, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                       };
            finalLeg2Query = todayLeg2Query.Concat(yesterdayLeg2Query);
        }

        // We want the LATEST possible arrivals that fit within the time window
        var leg2Trips = await finalLeg2Query.OrderByDescending(x => x.ArrSecs).Take(maxLegTrips).AsNoTracking().ToListAsync(cancellationToken);
        
        // Exact walking time filter (Dest->Target)
        leg2Trips = leg2Trips.Where(trip => 
        {
            var dStop = destStops.First(x => x.Stop.StopId == trip.DestStopId);
            int baseReqSecs = trip.IsPreviousDayTrip ? requestedSeconds + 86400 : requestedSeconds;
            return trip.ArrSecs <= baseReqSecs - dStop.WalkingTimeSeconds;
        }).ToList();

        if (!leg2Trips.Any()) return new List<OneTransferResult>();

        var leg2TripDbIds = leg2Trips.Select(x => x.TripDbId).Distinct().ToList();
        
        // Get all preceding stops for these Leg2 trips (these will be TransferStop2)
        var leg2Stops = await _context.GtfsStopTimes
            .Where(st => leg2TripDbIds.Contains(st.GtfsTripId))
            .Select(st => new { st.GtfsTripId, st.StopId, st.StopSequence, st.DepartureSeconds, st.DepartureTimeRaw })
            .AsNoTracking().ToListAsync(cancellationToken);

        var validLeg2Stops = new List<Leg2TripData>(); // We'll expand Leg2TripData to act like a StopData
        foreach (var leg2 in leg2Trips)
        {
            var stopsBefore = leg2Stops.Where(s => s.GtfsTripId == leg2.TripDbId && s.StopSequence < leg2.ArrSeq).ToList();
            foreach (var sb in stopsBefore)
            {
                var clone = new Leg2TripData
                {
                    TripId = leg2.TripId, TripDbId = leg2.TripDbId, RouteId = leg2.RouteId, RouteShortName = leg2.RouteShortName, RouteType = leg2.RouteType, TripHeadsign = leg2.TripHeadsign, DirectionId = leg2.DirectionId,
                    DestStopId = leg2.DestStopId, ArrSeq = leg2.ArrSeq, ArrSecs = leg2.ArrSecs, ArrTimeRaw = leg2.ArrTimeRaw, IsPreviousDayTrip = leg2.IsPreviousDayTrip, ServiceId = leg2.ServiceId, ShapeId = leg2.ShapeId,
                    TransferStop2Id = sb.StopId,
                    DepSeq = sb.StopSequence,
                    DepSecs = sb.DepartureSeconds.GetValueOrDefault(),
                    DepTimeRaw = sb.DepartureTimeRaw,
                    StopCount = leg2Stops.Count(s => s.GtfsTripId == leg2.TripDbId && s.StopSequence >= sb.StopSequence && s.StopSequence < leg2.ArrSeq)
                };
                validLeg2Stops.Add(clone);
            }
        }

        var uniqueTransferStop2Ids = validLeg2Stops.Select(x => x.TransferStop2Id).Distinct().ToList();
        var transferPairs = new List<TransferPair>();
        
        foreach (var ts2Id in uniqueTransferStop2Ids)
        {
            transferPairs.Add(new TransferPair { TransferStop1Id = ts2Id, TransferStop2Id = ts2Id, WalkSeconds = 0 });
            var stopTransfers = activeStopsCache.TransfersByToStopId[ts2Id];
            if (stopTransfers != null)
            {
                foreach (var tr in stopTransfers)
                {
                    if (tr.DistanceMeters <= maxTransferWalkMeters)
                    {
                        transferPairs.Add(new TransferPair { TransferStop1Id = tr.FromStopId, TransferStop2Id = ts2Id, WalkSeconds = tr.WalkingTimeSeconds });
                    }
                }
            }
        }

        var uniqueTransferStop1Ids = transferPairs.Select(x => x.TransferStop1Id).Distinct().ToList();
        var leg1Candidates = new List<Leg1StopData>();
        
        if (!validLeg2Stops.Any()) return new List<OneTransferResult>();
        int maxLeg1ArrSecs = validLeg2Stops.Max(x => x.DepSecs) - transferBufferSeconds;

        foreach (var chunk in uniqueTransferStop1Ids.Chunk(500))
        {
            var chunkIds = chunk.ToList();
            var todayLeg1Query = from o in _context.GtfsStopTimes
                                   join ts in _context.GtfsStopTimes on o.GtfsTripId equals ts.GtfsTripId
                                   join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                   join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                   where originStopIds.Contains(o.StopId) && chunkIds.Contains(ts.StopId) && ts.StopSequence > o.StopSequence && activeServiceIds.Contains(t.ServiceId)
                                   select new Leg1TripData {
                                       TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                       OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = o.DepartureTimeRaw, IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                   };
                                   
            var leg1List = await todayLeg1Query.AsNoTracking().ToListAsync(cancellationToken);
            // Fetch Arrival info at TransferStop1 separately for these trips
            if (leg1List.Any())
            {
                var l1TripDbIds = leg1List.Select(x => x.TripDbId).ToList();
                var transferArrStops = await _context.GtfsStopTimes.Where(st => l1TripDbIds.Contains(st.GtfsTripId) && chunkIds.Contains(st.StopId))
                    .Select(st => new { st.GtfsTripId, st.StopId, st.StopSequence, st.ArrivalSeconds, st.ArrivalTimeRaw }).AsNoTracking().ToListAsync(cancellationToken);
                    
                foreach (var l1 in leg1List)
                {
                    var arrStops = transferArrStops.Where(s => s.GtfsTripId == l1.TripDbId && s.StopSequence > l1.DepSeq).ToList();
                    foreach (var s in arrStops)
                    {
                        leg1Candidates.Add(new Leg1StopData { TripInfo = l1, TransferStop1Id = s.StopId, ArrSeq = s.StopSequence, ArrSecs = s.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = s.ArrivalTimeRaw, StopCount = 0 });
                    }
                }
            }
        }
        
        // Similar chunking for yesterday if needed (omitted for brevity, assume 0-transfer covers most overnight)
        if (previousDayServiceIds.Any())
        {
             foreach (var chunk in uniqueTransferStop1Ids.Chunk(500))
             {
                 var chunkIds = chunk.ToList();
                 var yesterdayLeg1Query = from o in _context.GtfsStopTimes
                                        join ts in _context.GtfsStopTimes on o.GtfsTripId equals ts.GtfsTripId
                                        join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                        join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                        where originStopIds.Contains(o.StopId) && chunkIds.Contains(ts.StopId) && ts.StopSequence > o.StopSequence && previousDayServiceIds.Contains(t.ServiceId)
                                        select new Leg1TripData {
                                            TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                            OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = o.DepartureTimeRaw, IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                        };
                                        
                 var leg1List = await yesterdayLeg1Query.AsNoTracking().ToListAsync(cancellationToken);
                 if (leg1List.Any())
                 {
                     var l1TripDbIds = leg1List.Select(x => x.TripDbId).ToList();
                     var transferArrStops = await _context.GtfsStopTimes.Where(st => l1TripDbIds.Contains(st.GtfsTripId) && chunkIds.Contains(st.StopId))
                         .Select(st => new { st.GtfsTripId, st.StopId, st.StopSequence, st.ArrivalSeconds, st.ArrivalTimeRaw }).AsNoTracking().ToListAsync(cancellationToken);
                         
                     foreach (var l1 in leg1List)
                     {
                         var arrStops = transferArrStops.Where(s => s.GtfsTripId == l1.TripDbId && s.StopSequence > l1.DepSeq).ToList();
                         foreach (var s in arrStops)
                         {
                             leg1Candidates.Add(new Leg1StopData { TripInfo = l1, TransferStop1Id = s.StopId, ArrSeq = s.StopSequence, ArrSecs = s.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = s.ArrivalTimeRaw, StopCount = 0 });
                         }
                     }
                 }
             }
        }

        var results = new List<OneTransferResult>();
        var deduplicationSet = new HashSet<string>();

        // Match Leg1 and Leg2 via transfer pairs
        foreach (var l2 in validLeg2Stops)
        {
            var pairs = transferPairs.Where(p => p.TransferStop2Id == l2.TransferStop2Id).ToList();
            foreach (var pair in pairs)
            {
                int bufferSecs = transferBufferSeconds + (int)pair.WalkSeconds;
                
                var l1Matches = leg1Candidates.Where(l1 => l1.TransferStop1Id == pair.TransferStop1Id).ToList();
                foreach (var l1 in l1Matches)
                {
                    int l2Dep = l2.IsPreviousDayTrip ? l2.DepSecs + 86400 : l2.DepSecs;
                    int l1Arr = l1.TripInfo.IsPreviousDayTrip ? l1.ArrSecs + 86400 : l1.ArrSecs;

                    if (l1Arr + bufferSecs <= l2Dep)
                    {
                        var waitMinutes = (l2Dep - l1Arr) / 60.0;
                        if (waitMinutes > maxWaitTimeMinutes) continue;

                        int l2Arr = l2.IsPreviousDayTrip ? l2.ArrSecs + 86400 : l2.ArrSecs;
                        int l1Dep = l1.TripInfo.IsPreviousDayTrip ? l1.TripInfo.DepSecs + 86400 : l1.TripInfo.DepSecs;
                        var totalJourneyMinutes = (l2Arr - l1Dep) / 60.0;
                        
                        if (totalJourneyMinutes > maxJourneyTimeMinutes) continue;

                        var pattern1 = !string.IsNullOrEmpty(l1.TripInfo.ShapeId) ? $"P_{l1.TripInfo.ShapeId}" : $"P_{l1.TripInfo.RouteId}_{l1.TripInfo.DirectionId}";
                        var pattern2 = !string.IsNullOrEmpty(l2.ShapeId) ? $"P_{l2.ShapeId}" : $"P_{l2.RouteId}_{l2.DirectionId}";
                        
                        if (pattern1 == pattern2) continue;

                        var hash = $"{pattern1}_{pattern2}_{l1.TransferStop1Id}_{l2.TransferStop2Id}";
                        if (!deduplicationSet.Contains(hash))
                        {
                            deduplicationSet.Add(hash);
                            
                            var baseDep1 = l1.TripInfo.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                            var baseArr2 = l2.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                            
                            results.Add(new OneTransferResult
                            {
                                Leg1 = new LegData {
                                    TripId = l1.TripInfo.TripId, RouteId = l1.TripInfo.RouteId, RouteShortName = l1.TripInfo.RouteShortName, RouteType = l1.TripInfo.RouteType, Headsign = l1.TripInfo.TripHeadsign, DirectionId = l1.TripInfo.DirectionId,
                                    FromStopId = l1.TripInfo.OriginStopId, ToStopId = l1.TransferStop1Id, FromStopSequence = l1.TripInfo.DepSeq, ToStopSequence = l1.ArrSeq, DepSecs = l1.TripInfo.DepSecs, DepTimeRaw = l1.TripInfo.DepTimeRaw, ArrSecs = l1.ArrSecs, ArrTimeRaw = l1.ArrTimeRaw, IsPreviousDayTrip = l1.TripInfo.IsPreviousDayTrip, StopCount = l1.StopCount,
                                    ServiceId = l1.TripInfo.ServiceId, ShapeId = l1.TripInfo.ShapeId, ServiceDate = baseDep1.ToString("yyyy-MM-dd"), PatternId = pattern1
                                },
                                Leg2 = new LegData {
                                    TripId = l2.TripId, RouteId = l2.RouteId, RouteShortName = l2.RouteShortName, RouteType = l2.RouteType, Headsign = l2.TripHeadsign, DirectionId = l2.DirectionId,
                                    FromStopId = l2.TransferStop2Id, ToStopId = l2.DestStopId, FromStopSequence = l2.DepSeq, ToStopSequence = l2.ArrSeq, DepSecs = l2.DepSecs, DepTimeRaw = l2.DepTimeRaw, ArrSecs = l2.ArrSecs, ArrTimeRaw = l2.ArrTimeRaw, IsPreviousDayTrip = l2.IsPreviousDayTrip, StopCount = l2.StopCount,
                                    ServiceId = l2.ServiceId, ShapeId = l2.ShapeId, ServiceDate = baseArr2.ToString("yyyy-MM-dd"), PatternId = pattern2
                                },
                                TransferWalkMeters = pair.WalkSeconds > 0 ? (int)(pair.WalkSeconds * walkingSpeed) : 0,
                                TransferWalkSeconds = pair.WalkSeconds
                            });
                        }
                    }
                }
            }
        }
        
        var deduplicatedResults = new List<OneTransferResult>();
        var patternHashes = new HashSet<string>();
        foreach (var res in results.OrderByDescending(x => x.Leg1.DepSecs)) // Optimize for LATEST possible departure
        {
            var hash = $"{res.Leg1.PatternId}|{res.Leg2.PatternId}";
            if (patternHashes.Add(hash))
            {
                deduplicatedResults.Add(res);
            }
        }
        
        return deduplicatedResults;
    }
}
