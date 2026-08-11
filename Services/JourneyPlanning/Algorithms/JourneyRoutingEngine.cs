using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using TransportDataService;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services.JourneyPlanning.Models;

namespace ulasim_veri_servisi.Services.JourneyPlanning.Algorithms;

public interface IJourneyRoutingEngine
{
    Task<List<DirectTripResult>> FindDirectTripsAsync(List<string> originStopIds, List<string> destStopIds, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int maxJourneyTimeMinutes, CancellationToken cancellationToken);
    Task<List<OneTransferResult>> FindOneTransferTripsAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int transferBufferSeconds, DateTime targetDate, TimeZoneInfo tzi, ActiveStopsCache activeStopsCache, int maxTransferWalkMeters, double walkingSpeed, int maxLegTrips, int maxTransferTrips, int maxWaitTimeMinutes, int maxJourneyTimeMinutes, CancellationToken cancellationToken);
    Task<List<TwoTransferResult>> FindTwoTransferTripsAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int transferBufferSeconds, DateTime targetDate, TimeZoneInfo tzi, ActiveStopsCache activeStopsCache, int maxTransferWalkMeters, double walkingSpeed, int maxLegTrips, int maxTwoTransferTrips, int maxWaitTimeMinutes, int maxJourneyTimeMinutes, CancellationToken cancellationToken);
}

public class JourneyRoutingEngine : IJourneyRoutingEngine
{
    private readonly AppDbContext _context;
    private readonly ILogger<JourneyRoutingEngine> _logger;

    public JourneyRoutingEngine(AppDbContext context, ILogger<JourneyRoutingEngine> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<DirectTripResult>> FindDirectTripsAsync(List<string> originStopIds, List<string> destStopIds, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int maxJourneyTimeMinutes, CancellationToken cancellationToken)
    {
        int minDepartureSeconds = requestedSeconds + minWalkingTime;
        int maxDepartureSeconds = requestedSeconds + (maxJourneyTimeMinutes * 60);
        var todayQuery = from o in _context.GtfsStopTimes
                         join d in _context.GtfsStopTimes on o.GtfsTripId equals d.GtfsTripId
                         join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                         join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                         where originStopIds.Contains(o.StopId) &&
                               destStopIds.Contains(d.StopId) &&
                               d.StopSequence > o.StopSequence &&
                               activeServiceIds.Contains(t.ServiceId) &&
                               o.DepartureSeconds >= minDepartureSeconds && o.DepartureSeconds <= maxDepartureSeconds
                         select new DirectTripResult
                         {
                             TripId = t.TripId,
                             RouteId = r.RouteId,
                             RouteShortName = r.RouteShortName, RouteType = r.RouteType,
                             TripHeadsign = t.TripHeadsign,
                             DirectionId = t.DirectionId,
                             OriginStopId = o.StopId,
                             DestStopId = d.StopId,
                             OriginStopSequence = o.StopSequence,
                             DestStopSequence = d.StopSequence,
                             DepartureSeconds = o.DepartureSeconds.GetValueOrDefault(),
                             DepartureTimeRaw = o.DepartureTimeRaw,
                             ArrivalSeconds = d.ArrivalSeconds.GetValueOrDefault(),
                             ArrivalTimeRaw = d.ArrivalTimeRaw,
                             IsPreviousDayTrip = false,
                             ServiceId = t.ServiceId,
                             ShapeId = t.ShapeId,
                             StopCount = _context.GtfsStopTimes.Count(s => s.GtfsTripId == t.Id && s.StopSequence > o.StopSequence && s.StopSequence <= d.StopSequence)
                         };

        IQueryable<DirectTripResult> finalQuery = todayQuery;

        if (previousDayServiceIds.Any())
        {
            int previousDayMinDepartureSeconds = requestedSeconds + 86400 + minWalkingTime;
            int previousDayMaxDepartureSeconds = requestedSeconds + 86400 + (maxJourneyTimeMinutes * 60);
            var yesterdayQuery = from o in _context.GtfsStopTimes
                                 join d in _context.GtfsStopTimes on o.GtfsTripId equals d.GtfsTripId
                                 join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                 join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                 where originStopIds.Contains(o.StopId) &&
                                       destStopIds.Contains(d.StopId) &&
                                       d.StopSequence > o.StopSequence &&
                                       previousDayServiceIds.Contains(t.ServiceId) &&
                                       o.DepartureSeconds >= previousDayMinDepartureSeconds && o.DepartureSeconds <= previousDayMaxDepartureSeconds
                                 select new DirectTripResult
                                 {
                                     TripId = t.TripId,
                                     RouteId = r.RouteId,
                                     RouteShortName = r.RouteShortName, RouteType = r.RouteType,
                                     TripHeadsign = t.TripHeadsign,
                                     DirectionId = t.DirectionId,
                                     OriginStopId = o.StopId,
                                     DestStopId = d.StopId,
                                     OriginStopSequence = o.StopSequence,
                                     DestStopSequence = d.StopSequence,
                                     DepartureSeconds = o.DepartureSeconds.GetValueOrDefault(),
                                     DepartureTimeRaw = o.DepartureTimeRaw,
                                     ArrivalSeconds = d.ArrivalSeconds.GetValueOrDefault(),
                                     ArrivalTimeRaw = d.ArrivalTimeRaw,
                                     IsPreviousDayTrip = true,
                                     ServiceId = t.ServiceId,
                                     ShapeId = t.ShapeId,
                                     StopCount = _context.GtfsStopTimes.Count(s => s.GtfsTripId == t.Id && s.StopSequence > o.StopSequence && s.StopSequence <= d.StopSequence)
                                 };
            finalQuery = todayQuery.Concat(yesterdayQuery);
        }

        return await finalQuery.OrderBy(x => x.DepartureSeconds).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<OneTransferResult>> FindOneTransferTripsAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int transferBufferSeconds, DateTime targetDate, TimeZoneInfo tzi, ActiveStopsCache activeStopsCache, int maxTransferWalkMeters, double walkingSpeed, int maxLegTrips, int maxTransferTrips, int maxWaitTimeMinutes, int maxJourneyTimeMinutes, CancellationToken cancellationToken)
    {
        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();
        int minDepartureSeconds = requestedSeconds + minWalkingTime;
        int maxDepartureSeconds = requestedSeconds + (maxWaitTimeMinutes * 60) + (maxJourneyTimeMinutes * 60);
        var todayLeg1Query = from o in _context.GtfsStopTimes
                               join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                               join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                               where originStopIds.Contains(o.StopId) &&
                                     activeServiceIds.Contains(t.ServiceId) &&
                                     o.DepartureSeconds >= minDepartureSeconds && o.DepartureSeconds <= maxDepartureSeconds
                                   select new Leg1TripData {
                                       TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                       OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = o.DepartureTimeRaw, IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                   };

        IQueryable<Leg1TripData> finalLeg1Query = todayLeg1Query;

        if (previousDayServiceIds.Any())
        {
            int previousDayMinDepartureSeconds = requestedSeconds + 86400 + minWalkingTime;
            int previousDayMaxDepartureSeconds = requestedSeconds + 86400 + (maxWaitTimeMinutes * 60) + (maxJourneyTimeMinutes * 60);
            var yesterdayLeg1Query = from o in _context.GtfsStopTimes
                                       join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                       join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                       where originStopIds.Contains(o.StopId) &&
                                             previousDayServiceIds.Contains(t.ServiceId) &&
                                             o.DepartureSeconds >= previousDayMinDepartureSeconds && o.DepartureSeconds <= previousDayMaxDepartureSeconds
                                       select new Leg1TripData {
                                           TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                           OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = o.DepartureTimeRaw, IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                       };
            finalLeg1Query = todayLeg1Query.Concat(yesterdayLeg1Query);
        }

        var leg1Trips = await finalLeg1Query.OrderBy(x => x.DepSecs).AsNoTracking().ToListAsync(cancellationToken);
        
        // Exact walking time filter in memory for leg1
        leg1Trips = leg1Trips.Where(trip => 
        {
            var oStop = originStops.First(x => x.Stop.StopId == trip.OriginStopId);
            int baseReqSecs = trip.IsPreviousDayTrip ? requestedSeconds + 86400 : requestedSeconds;
            return trip.DepSecs >= baseReqSecs + oStop.WalkingTimeSeconds;
        }).ToList();

        if (!leg1Trips.Any()) return new List<OneTransferResult>();

        var leg1TripDbIds = leg1Trips.Select(x => x.TripDbId).Distinct().ToList();
        var leg1Stops = await _context.GtfsStopTimes
            .Where(st => leg1TripDbIds.Contains(st.GtfsTripId))
            .Select(st => new { st.GtfsTripId, st.StopId, st.StopSequence, st.ArrivalSeconds, st.ArrivalTimeRaw })
            .AsNoTracking().ToListAsync(cancellationToken);

        var validLeg1Stops = new List<Leg1StopData>();
        foreach (var leg1 in leg1Trips)
        {
            var stopsAfter = leg1Stops.Where(s => s.GtfsTripId == leg1.TripDbId && s.StopSequence > leg1.DepSeq).ToList();
            foreach (var sa in stopsAfter)
            {
                validLeg1Stops.Add(new Leg1StopData { TripInfo = leg1, TransferStop1Id = sa.StopId, ArrSeq = sa.StopSequence, ArrSecs = sa.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = sa.ArrivalTimeRaw, StopCount = leg1Stops.Count(s => s.GtfsTripId == leg1.TripDbId && s.StopSequence > leg1.DepSeq && s.StopSequence <= sa.StopSequence) });
            }
        }

        var uniqueTransferStop1Ids = validLeg1Stops.Select(x => x.TransferStop1Id).Distinct().ToList();
        var transferPairs = new List<TransferPair>();
        foreach (var ts1Id in uniqueTransferStop1Ids)
        {
            transferPairs.Add(new TransferPair { TransferStop1Id = ts1Id, TransferStop2Id = ts1Id, WalkSeconds = 0 });
            if (activeStopsCache.TransfersByStopId.TryGetValue(ts1Id, out var stopTransfers))
            {
                foreach (var tr in stopTransfers)
                {
                    if (tr.DistanceMeters <= maxTransferWalkMeters)
                    {
                        transferPairs.Add(new TransferPair { TransferStop1Id = ts1Id, TransferStop2Id = tr.ToStopId, WalkSeconds = tr.WalkingTimeSeconds });
                    }
                }
            }
        }

        var uniqueTransferStop2Ids = transferPairs.Select(x => x.TransferStop2Id).Distinct().ToList();
        var leg2Candidates = new List<Leg2TripData>();
        
        if (!validLeg1Stops.Any()) return new List<OneTransferResult>();
        int minLeg2DepSecs = validLeg1Stops.Min(x => x.ArrSecs) + transferBufferSeconds;

        foreach (var chunk in uniqueTransferStop2Ids.Chunk(500))
        {
            var chunkIds = chunk.ToList();
            var todayLeg2Query = from ts in _context.GtfsStopTimes
                                   join d in _context.GtfsStopTimes on ts.GtfsTripId equals d.GtfsTripId
                                   join t in _context.GtfsTrips on ts.GtfsTripId equals t.Id
                                   join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                   where chunkIds.Contains(ts.StopId) && destStopIds.Contains(d.StopId) && d.StopSequence > ts.StopSequence && activeServiceIds.Contains(t.ServiceId)
                                   select new Leg2TripData {
                                       TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                       TransferStop2Id = ts.StopId, DestStopId = d.StopId, DepSeq = ts.StopSequence, ArrSeq = d.StopSequence, DepSecs = ts.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = ts.DepartureTimeRaw, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId, StopCount = 0
                                   };
                                   
            IQueryable<Leg2TripData> finalLeg2Query = todayLeg2Query;
            
            if (previousDayServiceIds.Any())
            {
                var yesterdayLeg2Query = from ts in _context.GtfsStopTimes
                                       join d in _context.GtfsStopTimes on ts.GtfsTripId equals d.GtfsTripId
                                       join t in _context.GtfsTrips on ts.GtfsTripId equals t.Id
                                       join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                       where chunkIds.Contains(ts.StopId) && destStopIds.Contains(d.StopId) && d.StopSequence > ts.StopSequence && previousDayServiceIds.Contains(t.ServiceId)
                                       select new Leg2TripData {
                                           TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                           TransferStop2Id = ts.StopId, DestStopId = d.StopId, DepSeq = ts.StopSequence, ArrSeq = d.StopSequence, DepSecs = ts.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = ts.DepartureTimeRaw, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId, StopCount = 0
                                       };
                finalLeg2Query = todayLeg2Query.Concat(yesterdayLeg2Query);
            }

            var chunkResults = await finalLeg2Query.AsNoTracking().ToListAsync(cancellationToken);
            var chunkTripIds = chunkResults.Select(x => x.TripDbId).Distinct().ToList();
            var chunkSummaries = await _context.GtfsTripStopSummaries
                .Where(x => chunkTripIds.Contains(x.GtfsTripId))
                .ToDictionaryAsync(x => x.GtfsTripId, x => x.StopSequences, cancellationToken);
                
            foreach(var leg2 in chunkResults)
            {
                if (chunkSummaries.TryGetValue(leg2.TripDbId, out var seqs))
                {
                    leg2.StopCount = seqs.Count(s => s > leg2.DepSeq && s <= leg2.ArrSeq);
                }
            }
            leg2Candidates.AddRange(chunkResults);
        }

        var results = new List<OneTransferResult>();
        var deduplicationSet = new HashSet<string>();

        var transferPairsLookup = transferPairs.ToLookup(p => p.TransferStop1Id);
        var leg2CandidatesLookup = leg2Candidates.ToLookup(l2 => l2.TransferStop2Id);

        foreach (var l1 in validLeg1Stops)
        {
            var pairs = transferPairsLookup[l1.TransferStop1Id];
            foreach (var pair in pairs)
            {
                var l2s = leg2CandidatesLookup[pair.TransferStop2Id];
                foreach (var l2 in l2s)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (l1.TripInfo.TripId == l2.TripId) continue;

                    var baseDate1 = l1.TripInfo.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                    var arrDt1 = baseDate1.AddSeconds(l1.ArrSecs);
                    var absArr1 = new DateTimeOffset(arrDt1, tzi.GetUtcOffset(arrDt1));

                    var baseDate2 = l2.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                    var depDt2 = baseDate2.AddSeconds(l2.DepSecs);
                    var absDep2 = new DateTimeOffset(depDt2, tzi.GetUtcOffset(depDt2));

                    if (absArr1.AddSeconds(pair.WalkSeconds + transferBufferSeconds) <= absDep2)
                    {
                        var waitMinutes = (absDep2 - absArr1).TotalMinutes;
                        if (waitMinutes > maxWaitTimeMinutes) continue;

                        var baseDep1 = l1.TripInfo.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                        var depDt1 = baseDep1.AddSeconds(l1.TripInfo.DepSecs);
                        var absDep1 = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1));

                        var baseArr2 = l2.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                        var arrDt2 = baseArr2.AddSeconds(l2.ArrSecs);
                        var absArr2 = new DateTimeOffset(arrDt2, tzi.GetUtcOffset(arrDt2));

                        var totalJourneyMinutes = (absArr2 - absDep1).TotalMinutes;
                        if (totalJourneyMinutes > maxJourneyTimeMinutes) continue;

                        var pattern1 = !string.IsNullOrEmpty(l1.TripInfo.ShapeId) ? $"P_{l1.TripInfo.ShapeId}" : $"P_{l1.TripInfo.RouteId}_{l1.TripInfo.DirectionId}";
                        var pattern2 = !string.IsNullOrEmpty(l2.ShapeId) ? $"P_{l2.ShapeId}" : $"P_{l2.RouteId}_{l2.DirectionId}";
                        
                        // Prevent transferring to the same pattern (e.g. getting off a bus and waiting for the next bus on the exact same route)
                        if (pattern1 == pattern2) continue;

                        var hash = $"{pattern1}_{pattern2}_{l1.TransferStop1Id}_{l2.TransferStop2Id}";
                        if (!deduplicationSet.Contains(hash))
                        {
                            deduplicationSet.Add(hash);
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
        foreach (var res in results.OrderBy(x => x.Leg2.ArrSecs))
        {
            var hash = $"{res.Leg1.PatternId}|{res.Leg2.PatternId}";
            _logger.LogWarning("[DEBUG-DEDUP] Leg1={Leg1TripId}, Leg2={Leg2TripId}, Hash={Hash}", res.Leg1.TripId, res.Leg2.TripId, hash);
            
            if (patternHashes.Add(hash))
            {
                deduplicatedResults.Add(res);
            }
            else 
            {
                _logger.LogWarning("[DEBUG-DEDUP] REJECTED Leg1={Leg1TripId}, Leg2={Leg2TripId} due to duplicate hash", res.Leg1.TripId, res.Leg2.TripId);
            }
        }
        _logger.LogWarning("[DEBUG-DEDUP] Total before={BeforeCount}, after={AfterCount}", results.Count, deduplicatedResults.Count);
        return deduplicatedResults;
    }
    public async Task<List<TwoTransferResult>> FindTwoTransferTripsAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int transferBufferSeconds, DateTime targetDate, TimeZoneInfo tzi, ActiveStopsCache activeStopsCache, int maxTransferWalkMeters, double walkingSpeed, int maxLegTrips, int maxTwoTransferTrips, int maxWaitTimeMinutes, int maxJourneyTimeMinutes, CancellationToken cancellationToken)
    {
        // 1. FORWARD: Leg 1 Trips
        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        int minDepartureSeconds = requestedSeconds + minWalkingTime;
        int maxDepartureSeconds = requestedSeconds + (maxWaitTimeMinutes * 60) + (maxJourneyTimeMinutes * 60);
        
        var todayLeg1Query = from o in _context.GtfsStopTimes
                               join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                               join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                               where originStopIds.Contains(o.StopId) && activeServiceIds.Contains(t.ServiceId) && o.DepartureSeconds >= minDepartureSeconds && o.DepartureSeconds <= maxDepartureSeconds
                               select new Leg1TripData {
                                   TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                   OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = o.DepartureTimeRaw, IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                               };

        IQueryable<Leg1TripData> finalLeg1Query = todayLeg1Query;
        if (previousDayServiceIds.Any())
        {
            int previousDayMinDepartureSeconds = requestedSeconds + 86400 + minWalkingTime;
            int previousDayMaxDepartureSeconds = requestedSeconds + 86400 + (maxWaitTimeMinutes * 60) + (maxJourneyTimeMinutes * 60);
            var yesterdayLeg1Query = from o in _context.GtfsStopTimes
                                       join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                       join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                       where originStopIds.Contains(o.StopId) && previousDayServiceIds.Contains(t.ServiceId) && o.DepartureSeconds >= previousDayMinDepartureSeconds && o.DepartureSeconds <= previousDayMaxDepartureSeconds
                                       select new Leg1TripData {
                                           TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                           OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = o.DepartureTimeRaw, IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                       };
            finalLeg1Query = todayLeg1Query.Concat(yesterdayLeg1Query);
        }

        var leg1Trips = await finalLeg1Query.OrderBy(x => x.DepSecs).Take(maxLegTrips).AsNoTracking().ToListAsync(cancellationToken);
        
        leg1Trips = leg1Trips.Where(trip => 
        {
            var oStop = originStops.First(x => x.Stop.StopId == trip.OriginStopId);
            int baseReqSecs = trip.IsPreviousDayTrip ? requestedSeconds + 86400 : requestedSeconds;
            return trip.DepSecs >= baseReqSecs + oStop.WalkingTimeSeconds;
        }).ToList();

        if (!leg1Trips.Any()) return new List<TwoTransferResult>();

        var leg1TripDbIds = leg1Trips.Select(x => x.TripDbId).Distinct().ToList();
        var leg1Stops = await _context.GtfsStopTimes
            .Where(st => leg1TripDbIds.Contains(st.GtfsTripId))
            .Select(st => new { st.GtfsTripId, st.StopId, st.StopSequence, st.ArrivalSeconds, st.ArrivalTimeRaw })
            .AsNoTracking().ToListAsync(cancellationToken);

        var validLeg1Stops = new List<Leg1StopData>();
        foreach (var leg1 in leg1Trips)
        {
            var stopsAfter = leg1Stops.Where(s => s.GtfsTripId == leg1.TripDbId && s.StopSequence > leg1.DepSeq).ToList();
            foreach (var sa in stopsAfter)
            {
                validLeg1Stops.Add(new Leg1StopData { TripInfo = leg1, TransferStop1Id = sa.StopId, ArrSeq = sa.StopSequence, ArrSecs = sa.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = sa.ArrivalTimeRaw, StopCount = leg1Stops.Count(s => s.GtfsTripId == leg1.TripDbId && s.StopSequence > leg1.DepSeq && s.StopSequence <= sa.StopSequence) });
            }
        }

        var uniqueTransferStop1Ids = validLeg1Stops.Select(x => x.TransferStop1Id).Distinct().ToList();
        var transfer1Pairs = new List<TransferPair>();
        foreach (var ts1Id in uniqueTransferStop1Ids)
        {
            transfer1Pairs.Add(new TransferPair { TransferStop1Id = ts1Id, TransferStop2Id = ts1Id, WalkSeconds = 0 });
            if (activeStopsCache.TransfersByStopId.TryGetValue(ts1Id, out var stopTransfers))
            {
                foreach (var tr in stopTransfers.Where(tr => tr.DistanceMeters <= maxTransferWalkMeters))
                {
                    transfer1Pairs.Add(new TransferPair { TransferStop1Id = ts1Id, TransferStop2Id = tr.ToStopId, WalkSeconds = tr.WalkingTimeSeconds });
                }
            }
        }
        var validLeg2OriginStopIds = transfer1Pairs.Select(x => x.TransferStop2Id).Distinct().ToList();


        // 2. BACKWARD: Leg 3 Trips
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();
        var todayLeg3Query = from d in _context.GtfsStopTimes
                             join t in _context.GtfsTrips on d.GtfsTripId equals t.Id
                             join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                             where destStopIds.Contains(d.StopId) && activeServiceIds.Contains(t.ServiceId)
                             select new Leg3TripData {
                                 TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                 DestStopId = d.StopId, ArrSeq = d.StopSequence, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                             };

        IQueryable<Leg3TripData> finalLeg3Query = todayLeg3Query;
        if (previousDayServiceIds.Any())
        {
            var yesterdayLeg3Query = from d in _context.GtfsStopTimes
                                     join t in _context.GtfsTrips on d.GtfsTripId equals t.Id
                                     join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                     where destStopIds.Contains(d.StopId) && previousDayServiceIds.Contains(t.ServiceId)
                                     select new Leg3TripData {
                                         TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                         DestStopId = d.StopId, ArrSeq = d.StopSequence, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId
                                     };
            finalLeg3Query = todayLeg3Query.Concat(yesterdayLeg3Query);
        }

        var leg3Trips = await finalLeg3Query.OrderBy(x => x.ArrSecs).AsNoTracking().ToListAsync(cancellationToken);
        if (!leg3Trips.Any()) return new List<TwoTransferResult>();

        var leg3TripDbIds = leg3Trips.Select(x => x.TripDbId).Distinct().ToList();
        var leg3Stops = await _context.GtfsStopTimes
            .Where(st => leg3TripDbIds.Contains(st.GtfsTripId))
            .Select(st => new { st.GtfsTripId, st.StopId, st.StopSequence, st.DepartureSeconds, st.DepartureTimeRaw })
            .AsNoTracking().ToListAsync(cancellationToken);

        var validLeg3Stops = new List<Leg3StopData>();
        foreach (var leg3 in leg3Trips)
        {
            var stopsBefore = leg3Stops.Where(s => s.GtfsTripId == leg3.TripDbId && s.StopSequence < leg3.ArrSeq).ToList();
            foreach (var sb in stopsBefore)
            {
                validLeg3Stops.Add(new Leg3StopData { TripInfo = leg3, TransferStop2Id = sb.StopId, DepSeq = sb.StopSequence, DepSecs = sb.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = sb.DepartureTimeRaw, StopCount = leg3Stops.Count(s => s.GtfsTripId == leg3.TripDbId && s.StopSequence >= sb.StopSequence && s.StopSequence < leg3.ArrSeq) });
            }
        }

        var uniqueTransferStop2Ids = validLeg3Stops.Select(x => x.TransferStop2Id).Distinct().ToList();
        var transfer2Pairs = new List<TransferPair>();
        foreach (var ts2Id in uniqueTransferStop2Ids)
        {
            transfer2Pairs.Add(new TransferPair { TransferStop1Id = ts2Id, TransferStop2Id = ts2Id, WalkSeconds = 0 });
        }
        // Use the inverse index TransfersByToStopId to avoid full network scan
        foreach (var ts2Id in uniqueTransferStop2Ids)
        {
            foreach (var tr in activeStopsCache.TransfersByToStopId[ts2Id])
            {
                if (tr.DistanceMeters <= maxTransferWalkMeters)
                {
                    transfer2Pairs.Add(new TransferPair { TransferStop1Id = tr.FromStopId, TransferStop2Id = ts2Id, WalkSeconds = tr.WalkingTimeSeconds });
                }
            }
        }
        var validLeg2DestStopIds = transfer2Pairs.Select(x => x.TransferStop1Id).Distinct().ToList();

        // 3. MIDDLE: Leg 2 Trips (Origin in validLeg2OriginStopIds, Dest in validLeg2DestStopIds)
        var leg2Candidates = new List<Leg2TripData>();
        foreach (var chunkOrigin in validLeg2OriginStopIds.Chunk(500))
        {
            var chunkOriginIds = chunkOrigin.ToList();
            foreach (var chunkDest in validLeg2DestStopIds.Chunk(500))
            {
                var chunkDestIds = chunkDest.ToList();
                var todayLeg2Query = from ts in _context.GtfsStopTimes
                                     join d in _context.GtfsStopTimes on ts.GtfsTripId equals d.GtfsTripId
                                     join t in _context.GtfsTrips on ts.GtfsTripId equals t.Id
                                     join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                     where chunkOriginIds.Contains(ts.StopId) && chunkDestIds.Contains(d.StopId) && d.StopSequence > ts.StopSequence && activeServiceIds.Contains(t.ServiceId)
                                     select new Leg2TripData {
                                         TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                         TransferStop2Id = ts.StopId, DestStopId = d.StopId, DepSeq = ts.StopSequence, ArrSeq = d.StopSequence, DepSecs = ts.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = ts.DepartureTimeRaw, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = false, ServiceId = t.ServiceId, ShapeId = t.ShapeId, StopCount = 0
                                     };
                
                IQueryable<Leg2TripData> finalLeg2Query = todayLeg2Query;
                if (previousDayServiceIds.Any())
                {
                    var yesterdayLeg2Query = from ts in _context.GtfsStopTimes
                                             join d in _context.GtfsStopTimes on ts.GtfsTripId equals d.GtfsTripId
                                             join t in _context.GtfsTrips on ts.GtfsTripId equals t.Id
                                             join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                             where chunkOriginIds.Contains(ts.StopId) && chunkDestIds.Contains(d.StopId) && d.StopSequence > ts.StopSequence && previousDayServiceIds.Contains(t.ServiceId)
                                             select new Leg2TripData {
                                                 TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName, RouteType = r.RouteType, TripHeadsign = t.TripHeadsign, DirectionId = t.DirectionId,
                                                 TransferStop2Id = ts.StopId, DestStopId = d.StopId, DepSeq = ts.StopSequence, ArrSeq = d.StopSequence, DepSecs = ts.DepartureSeconds.GetValueOrDefault(), DepTimeRaw = ts.DepartureTimeRaw, ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), ArrTimeRaw = d.ArrivalTimeRaw, IsPreviousDayTrip = true, ServiceId = t.ServiceId, ShapeId = t.ShapeId, StopCount = 0
                                             };
                    finalLeg2Query = todayLeg2Query.Concat(yesterdayLeg2Query);
                }

                var chunkResults = await finalLeg2Query.AsNoTracking().ToListAsync(cancellationToken);
                var chunkTripIds = chunkResults.Select(x => x.TripDbId).Distinct().ToList();
                var chunkSummaries = await _context.GtfsTripStopSummaries
                    .Where(x => chunkTripIds.Contains(x.GtfsTripId))
                    .ToDictionaryAsync(x => x.GtfsTripId, x => x.StopSequences, cancellationToken);
                    
                foreach(var leg2 in chunkResults)
                {
                    if (chunkSummaries.TryGetValue(leg2.TripDbId, out var seqs))
                    {
                        leg2.StopCount = seqs.Count(s => s > leg2.DepSeq && s <= leg2.ArrSeq);
                    }
                }
                leg2Candidates.AddRange(chunkResults);
            }
        }

        // 4. In-Memory Time & Loop Filtering
        var results = new List<TwoTransferResult>();
        var deduplicationSet = new HashSet<string>();

        var transfer1PairsLookup = transfer1Pairs.ToLookup(p => p.TransferStop1Id);
        var leg2CandidatesLookup = leg2Candidates.ToLookup(l2 => l2.TransferStop2Id);
        var transfer2PairsLookup = transfer2Pairs.ToLookup(p => p.TransferStop1Id);
        var validLeg3StopsLookup = validLeg3Stops.ToLookup(l3 => l3.TransferStop2Id);

        foreach (var l1 in validLeg1Stops)
        {
            var p1List = transfer1PairsLookup[l1.TransferStop1Id];
            foreach (var p1 in p1List)
            {
                var l2List = leg2CandidatesLookup[p1.TransferStop2Id];
                foreach (var l2 in l2List)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (l1.TripInfo.TripId == l2.TripId || l1.TripInfo.RouteId == l2.RouteId) continue; // Loop Prevention

                    var baseDate1 = l1.TripInfo.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                    var arrDt1 = baseDate1.AddSeconds(l1.ArrSecs);
                    var absArr1 = new DateTimeOffset(arrDt1, tzi.GetUtcOffset(arrDt1));

                    var baseDate2 = l2.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                    var depDt2 = baseDate2.AddSeconds(l2.DepSecs);
                    var absDep2 = new DateTimeOffset(depDt2, tzi.GetUtcOffset(depDt2));

                    if (absArr1.AddSeconds(p1.WalkSeconds + transferBufferSeconds) <= absDep2)
                    {
                        var wait1Minutes = (absDep2 - absArr1).TotalMinutes;
                        if (wait1Minutes > maxWaitTimeMinutes) continue;

                        var p2List = transfer2PairsLookup[l2.DestStopId];
                        foreach (var p2 in p2List)
                        {
                            var l3List = validLeg3StopsLookup[p2.TransferStop2Id];
                            foreach (var l3 in l3List)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (l2.TripId == l3.TripInfo.TripId || l2.RouteId == l3.TripInfo.RouteId || l1.TripInfo.TripId == l3.TripInfo.TripId || l1.TripInfo.RouteId == l3.TripInfo.RouteId) continue; // Loop Prevention

                                var arrDt2 = baseDate2.AddSeconds(l2.ArrSecs);
                                var absArr2 = new DateTimeOffset(arrDt2, tzi.GetUtcOffset(arrDt2));

                                var baseDate3 = l3.TripInfo.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                                var depDt3 = baseDate3.AddSeconds(l3.DepSecs);
                                var absDep3 = new DateTimeOffset(depDt3, tzi.GetUtcOffset(depDt3));

                                if (absArr2.AddSeconds(p2.WalkSeconds + transferBufferSeconds) <= absDep3)
                                {
                                    var wait2Minutes = (absDep3 - absArr2).TotalMinutes;
                                    if (wait2Minutes > maxWaitTimeMinutes) continue;

                                    var baseDep1 = l1.TripInfo.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                                    var depDt1 = baseDep1.AddSeconds(l1.TripInfo.DepSecs);
                                    var absDep1 = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1));

                                    var baseArr3 = l3.TripInfo.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                                    var arrDt3 = baseArr3.AddSeconds(l3.TripInfo.ArrSecs);
                                    var absArr3 = new DateTimeOffset(arrDt3, tzi.GetUtcOffset(arrDt3));

                                    var totalJourneyMinutes = (absArr3 - absDep1).TotalMinutes;
                                    if (totalJourneyMinutes > maxJourneyTimeMinutes) continue;

                                    var pattern1 = !string.IsNullOrEmpty(l1.TripInfo.ShapeId) ? $"P_{l1.TripInfo.ShapeId}" : $"P_{l1.TripInfo.RouteId}_{l1.TripInfo.DirectionId}";
                                    var pattern2 = !string.IsNullOrEmpty(l2.ShapeId) ? $"P_{l2.ShapeId}" : $"P_{l2.RouteId}_{l2.DirectionId}";
                                    var pattern3 = !string.IsNullOrEmpty(l3.TripInfo.ShapeId) ? $"P_{l3.TripInfo.ShapeId}" : $"P_{l3.TripInfo.RouteId}_{l3.TripInfo.DirectionId}";
                                    
                                    // Prevent transferring to the same pattern on consecutive legs
                                    if (pattern1 == pattern2 || pattern2 == pattern3) continue;

                                    var hash = $"{pattern1}_{pattern2}_{pattern3}_{l1.TransferStop1Id}_{l2.DestStopId}_{l3.TripInfo.DestStopId}";
                                    if (!deduplicationSet.Contains(hash))
                                    {
                                        deduplicationSet.Add(hash);
                                        results.Add(new TwoTransferResult
                                        {
                                            Leg1 = new LegData { TripId = l1.TripInfo.TripId, RouteId = l1.TripInfo.RouteId, RouteShortName = l1.TripInfo.RouteShortName, RouteType = l1.TripInfo.RouteType, Headsign = l1.TripInfo.TripHeadsign, DirectionId = l1.TripInfo.DirectionId, FromStopId = l1.TripInfo.OriginStopId, ToStopId = l1.TransferStop1Id, FromStopSequence = l1.TripInfo.DepSeq, ToStopSequence = l1.ArrSeq, DepSecs = l1.TripInfo.DepSecs, DepTimeRaw = l1.TripInfo.DepTimeRaw, ArrSecs = l1.ArrSecs, ArrTimeRaw = l1.ArrTimeRaw, IsPreviousDayTrip = l1.TripInfo.IsPreviousDayTrip, StopCount = l1.StopCount, ServiceId = l1.TripInfo.ServiceId, ShapeId = l1.TripInfo.ShapeId, ServiceDate = baseDep1.ToString("yyyy-MM-dd"), PatternId = pattern1 },
                                            Leg2 = new LegData { TripId = l2.TripId, RouteId = l2.RouteId, RouteShortName = l2.RouteShortName, RouteType = l2.RouteType, Headsign = l2.TripHeadsign, DirectionId = l2.DirectionId, FromStopId = l2.TransferStop2Id, ToStopId = l2.DestStopId, FromStopSequence = l2.DepSeq, ToStopSequence = l2.ArrSeq, DepSecs = l2.DepSecs, DepTimeRaw = l2.DepTimeRaw, ArrSecs = l2.ArrSecs, ArrTimeRaw = l2.ArrTimeRaw, IsPreviousDayTrip = l2.IsPreviousDayTrip, StopCount = l2.StopCount, ServiceId = l2.ServiceId, ShapeId = l2.ShapeId, ServiceDate = baseDate2.ToString("yyyy-MM-dd"), PatternId = pattern2 },
                                            Leg3 = new LegData { TripId = l3.TripInfo.TripId, RouteId = l3.TripInfo.RouteId, RouteShortName = l3.TripInfo.RouteShortName, RouteType = l3.TripInfo.RouteType, Headsign = l3.TripInfo.TripHeadsign, DirectionId = l3.TripInfo.DirectionId, FromStopId = l3.TransferStop2Id, ToStopId = l3.TripInfo.DestStopId, FromStopSequence = l3.DepSeq, ToStopSequence = l3.TripInfo.ArrSeq, DepSecs = l3.DepSecs, DepTimeRaw = l3.DepTimeRaw, ArrSecs = l3.TripInfo.ArrSecs, ArrTimeRaw = l3.TripInfo.ArrTimeRaw, IsPreviousDayTrip = l3.TripInfo.IsPreviousDayTrip, StopCount = l3.StopCount, ServiceId = l3.TripInfo.ServiceId, ShapeId = l3.TripInfo.ShapeId, ServiceDate = baseDate3.ToString("yyyy-MM-dd"), PatternId = pattern3 },
                                            TransferWalk1Meters = p1.WalkSeconds > 0 ? (int)(p1.WalkSeconds * walkingSpeed) : 0, TransferWalk1Seconds = p1.WalkSeconds,
                                            TransferWalk2Meters = p2.WalkSeconds > 0 ? (int)(p2.WalkSeconds * walkingSpeed) : 0, TransferWalk2Seconds = p2.WalkSeconds
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        var deduplicatedResults = new List<TwoTransferResult>();
        var patternHashes = new HashSet<string>();
        foreach (var res in results.OrderBy(x => x.Leg3.ArrSecs))
        {
            var hash = $"{res.Leg1.PatternId}|{res.Leg2.PatternId}|{res.Leg3.PatternId}";
            if (patternHashes.Add(hash))
            {
                deduplicatedResults.Add(res);
            }
        }
        return deduplicatedResults;
    }
}
