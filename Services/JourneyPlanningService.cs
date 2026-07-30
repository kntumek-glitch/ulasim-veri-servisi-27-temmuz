using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TransportDataService;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasım_veri_servisi.Services.Interfaces;

namespace ulasım_veri_servisi.Services;

public class JourneyPlanningService : IJourneyPlanningService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public JourneyPlanningService(AppDbContext context, IMemoryCache cache, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _context = context;
        _cache = cache;
        _configuration = configuration;
    }

    public async Task<JourneyPlanSearchResponse> SearchJourneyAsync(JourneyPlanSearchRequest request, CancellationToken cancellationToken = default)
    {
        var response = new JourneyPlanSearchResponse();

        // 0. Check for Active Feed
        var activeRun = await _context.GtfsImportRuns
            .AsNoTracking()
            .Where(r => r.IsActive && r.Status == "Completed")
            .FirstOrDefaultAsync(cancellationToken);

        if (activeRun == null)
        {
            throw new ulasım_veri_servisi.Exceptions.ActiveFeedNotFoundException();
        }

        // Fetch agency info for timezone
        var agency = await _context.GtfsAgencies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        string timezone = agency?.AgencyTimezone ?? "Europe/Istanbul";

        // Fetch boundaries from calendars
        var minDate = await _context.GtfsCalendars.AsNoTracking().MinAsync(c => (DateOnly?)c.StartDate, cancellationToken);
        var maxDate = await _context.GtfsCalendars.AsNoTracking().MaxAsync(c => (DateOnly?)c.EndDate, cancellationToken);

        response.Metadata = new FeedMetadataDto
        {
            ActiveImportId = activeRun.Id,
            FeedHash = activeRun.FileHash ?? "UNKNOWN",
            Timezone = timezone,
            StartDate = minDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            EndDate = maxDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            IsStale = maxDate.HasValue && maxDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
        };

        // 1. Get Active Stops from Cache or DB
        var activeStops = await GetActiveStopsAsync(cancellationToken);
        if (!activeStops.Any()) return response;

        // 2. Load configurations
        int configMaxWalkingMeters = _configuration.GetValue<int>("JourneyPlan:MaxWalkingMeters", 1500);
        int finalMaxWalkingMeters = Math.Min(request.MaxWalkingMeters, configMaxWalkingMeters);
        double walkingSpeed = _configuration.GetValue<double>("JourneyPlan:WalkingSpeed", 1.4);
        int maxCandidateStops = _configuration.GetValue<int>("JourneyPlan:MaxCandidateStops", 5);
        int transferBufferMinutes = _configuration.GetValue<int>("JourneyPlan:TransferBufferMinutes", 3);
        int maxTransferWalkMeters = _configuration.GetValue<int>("JourneyPlan:MaxTransferWalkMeters", 500);

        // 3. Find Origin and Destination Stops within walking distance
        var originStops = FindStopsWithinRadius(activeStops, request.Origin.Lat, request.Origin.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);
        var destStops = FindStopsWithinRadius(activeStops, request.Destination.Lat, request.Destination.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);

        if (!originStops.Any() || !destStops.Any())
            return response;

        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();

        // 4. Resolve Target Date and Time in Local Timezone
        TimeZoneInfo tzi;
        try { tzi = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { tzi = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time"); }

        var localDateTime = TimeZoneInfo.ConvertTime(request.DepartureDateTime, tzi);
        var targetDate = localDateTime.Date;
        var requestedSeconds = (int)localDateTime.TimeOfDay.TotalSeconds;

        var activeServiceIds = await GetActiveServiceIdsAsync(targetDate, cancellationToken);
        var previousDayServiceIds = new List<string>();

        // If requested time is early morning, consider previous day's trips that go past midnight (24:00+)
        if (requestedSeconds < 4 * 3600)
        {
            previousDayServiceIds = await GetActiveServiceIdsAsync(targetDate.AddDays(-1), cancellationToken);
        }

        if (!activeServiceIds.Any() && !previousDayServiceIds.Any()) return response;

        var itineraries = new List<ItineraryDto>();

        // 5. Find Direct Routes (0-Transfer)
        int minWalkingTime = originStops.Min(x => x.WalkingTimeSeconds);
        var directTrips = await FindDirectTripsAsync(originStopIds, destStopIds, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, cancellationToken);
        
        // Exact walking time filter in memory
        var validTrips = directTrips.Where(trip => 
        {
            var oStop = originStops.First(x => x.Stop.StopId == trip.OriginStopId);
            int baseReqSecs = trip.IsPreviousDayTrip ? requestedSeconds + 86400 : requestedSeconds;
            return trip.DepartureSeconds >= baseReqSecs + oStop.WalkingTimeSeconds;
        });

        foreach (var trip in validTrips.Take(request.MaxResults))
        {
            var oStop = originStops.First(x => x.Stop.StopId == trip.OriginStopId);
            var dStop = destStops.First(x => x.Stop.StopId == trip.DestStopId);
            
            DateTime baseDate = trip.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
            DateTime depDt = baseDate.AddSeconds(trip.DepartureSeconds);
            DateTime arrDt = baseDate.AddSeconds(trip.ArrivalSeconds);
            var departureTime = new DateTimeOffset(depDt, tzi.GetUtcOffset(depDt));
            var arrivalTime = new DateTimeOffset(arrDt, tzi.GetUtcOffset(arrDt));

            var leg = new LegDto
            {
                Mode = "TRANSIT",
                RouteId = trip.RouteId,
                RouteShortName = trip.RouteShortName,
                TripId = trip.TripId,
                FromStopId = trip.OriginStopId,
                FromStopName = oStop.Stop.StopName,
                DepartureTime = departureTime,
                ToStopId = trip.DestStopId,
                ToStopName = dStop.Stop.StopName,
                ArrivalTime = arrivalTime,
                DistanceMeters = 0, // Optional
                DurationMinutes = (trip.ArrivalSeconds - trip.DepartureSeconds) / 60
            };

            itineraries.Add(new ItineraryDto
            {
                DepartureTime = leg.DepartureTime.Value.AddSeconds(-oStop.WalkingTimeSeconds),
                ArrivalTime = leg.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                Transfers = 0,
                TotalWalkingMeters = oStop.DistanceMeters + dStop.DistanceMeters,
                Legs = new List<LegDto> { leg }
            });
        }

        // 6. If 1-Transfer is requested, implement 1-transfer logic
        if (request.MaxTransfers >= 1 && itineraries.Count < request.MaxResults)
        {
            var transferTrips = await FindOneTransferTripsAsync(originStops, destStops, activeStops, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, transferBufferMinutes * 60, maxTransferWalkMeters, walkingSpeed, cancellationToken);
            
            foreach (var tResult in transferTrips.Take(request.MaxResults - itineraries.Count))
            {
                var oStop = originStops.First(x => x.Stop.StopId == tResult.Leg1.FromStopId);
                var dStop = destStops.First(x => x.Stop.StopId == tResult.Leg2.ToStopId);
                
                DateTime baseDate1 = tResult.Leg1.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                DateTime depDt1 = baseDate1.AddSeconds(tResult.Leg1.DepSecs);
                DateTime arrDt1 = baseDate1.AddSeconds(tResult.Leg1.ArrSecs);
                
                DateTime baseDate2 = tResult.Leg2.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                DateTime depDt2 = baseDate2.AddSeconds(tResult.Leg2.DepSecs);
                DateTime arrDt2 = baseDate2.AddSeconds(tResult.Leg2.ArrSecs);

                var leg1 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg1.RouteId, RouteShortName = tResult.Leg1.RouteShortName, TripId = tResult.Leg1.TripId,
                    FromStopId = tResult.Leg1.FromStopId, FromStopName = oStop.Stop.StopName,
                    ToStopId = tResult.Leg1.ToStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg1.ToStopId).StopName,
                    DepartureTime = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1)),
                    ArrivalTime = new DateTimeOffset(arrDt1, tzi.GetUtcOffset(arrDt1)),
                    DurationMinutes = (tResult.Leg1.ArrSecs - tResult.Leg1.DepSecs) / 60
                };

                var leg2 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg2.RouteId, RouteShortName = tResult.Leg2.RouteShortName, TripId = tResult.Leg2.TripId,
                    FromStopId = tResult.Leg2.FromStopId, FromStopName = activeStops.First(s => s.StopId == tResult.Leg2.FromStopId).StopName,
                    ToStopId = tResult.Leg2.ToStopId, ToStopName = dStop.Stop.StopName,
                    DepartureTime = new DateTimeOffset(depDt2, tzi.GetUtcOffset(depDt2)),
                    ArrivalTime = new DateTimeOffset(arrDt2, tzi.GetUtcOffset(arrDt2)),
                    DurationMinutes = (tResult.Leg2.ArrSecs - tResult.Leg2.DepSecs) / 60
                };

                itineraries.Add(new ItineraryDto
                {
                    DepartureTime = leg1.DepartureTime.Value.AddSeconds(-oStop.WalkingTimeSeconds),
                    ArrivalTime = leg2.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                    Transfers = 1,
                    TotalWalkingMeters = oStop.DistanceMeters + dStop.DistanceMeters + tResult.TransferWalkMeters,
                    Legs = new List<LegDto> { leg1, leg2 }
                });
            }
        }

        response.Itineraries = itineraries.OrderBy(x => x.ArrivalTime).Take(request.MaxResults).ToList();
        return response;
    }

    private async Task<List<GtfsStop>> GetActiveStopsAsync(CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync("ActiveGtfsStops", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            entry.Size = 1; // Required if MemoryCache has a SizeLimit configured
            return await _context.GtfsStops.AsNoTracking().ToListAsync(cancellationToken);
        }) ?? new List<GtfsStop>();
    }

    private async Task<List<string>> GetActiveServiceIdsAsync(DateTime date, CancellationToken cancellationToken)
    {
        var targetDate = DateOnly.FromDateTime(date);
        var dayOfWeek = date.DayOfWeek;

        // In a real scenario, evaluate GtfsCalendar (monday..sunday) AND GtfsCalendarDates (exceptions).
        // Simplified query assuming GtfsCalendars is the main source of truth.
        var activeCalendars = await _context.GtfsCalendars
            .AsNoTracking()
            .Where(c => c.StartDate <= targetDate && c.EndDate >= targetDate)
            .ToListAsync(cancellationToken);

        var validServiceIds = activeCalendars.Where(c => 
            (dayOfWeek == DayOfWeek.Monday && c.Monday) ||
            (dayOfWeek == DayOfWeek.Tuesday && c.Tuesday) ||
            (dayOfWeek == DayOfWeek.Wednesday && c.Wednesday) ||
            (dayOfWeek == DayOfWeek.Thursday && c.Thursday) ||
            (dayOfWeek == DayOfWeek.Friday && c.Friday) ||
            (dayOfWeek == DayOfWeek.Saturday && c.Saturday) ||
            (dayOfWeek == DayOfWeek.Sunday && c.Sunday)
        ).Select(c => c.ServiceId).Where(s => s != null).Cast<string>().ToList();

        // Check exceptions
        var exceptions = await _context.GtfsCalendarDates
            .AsNoTracking()
            .Where(cd => cd.Date == targetDate)
            .ToListAsync(cancellationToken);

        foreach(var ex in exceptions)
        {
            if (ex.ExceptionType == 1 && !validServiceIds.Contains(ex.ServiceId)) validServiceIds.Add(ex.ServiceId);
            else if (ex.ExceptionType == 2) validServiceIds.Remove(ex.ServiceId);
        }

        return validServiceIds;
    }

    private async Task<List<DirectTripResult>> FindDirectTripsAsync(List<string> originStopIds, List<string> destStopIds, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, CancellationToken cancellationToken)
    {
        int minDepartureSeconds = requestedSeconds + minWalkingTime;
        var todayQuery = from o in _context.GtfsStopTimes
                         join d in _context.GtfsStopTimes on o.GtfsTripId equals d.GtfsTripId
                         join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                         join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                         where originStopIds.Contains(o.StopId) &&
                               destStopIds.Contains(d.StopId) &&
                               d.StopSequence > o.StopSequence &&
                               activeServiceIds.Contains(t.ServiceId) &&
                               o.DepartureSeconds >= minDepartureSeconds
                         select new DirectTripResult
                         {
                             TripId = t.TripId,
                             RouteId = r.RouteId,
                             RouteShortName = r.RouteShortName,
                             OriginStopId = o.StopId,
                             DestStopId = d.StopId,
                             DepartureSeconds = o.DepartureSeconds.GetValueOrDefault(),
                             ArrivalSeconds = d.ArrivalSeconds.GetValueOrDefault(),
                             IsPreviousDayTrip = false
                         };

        IQueryable<DirectTripResult> finalQuery = todayQuery;

        if (previousDayServiceIds.Any())
        {
            int previousDayMinDepartureSeconds = requestedSeconds + 86400 + minWalkingTime;
            var yesterdayQuery = from o in _context.GtfsStopTimes
                                 join d in _context.GtfsStopTimes on o.GtfsTripId equals d.GtfsTripId
                                 join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                 join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                 where originStopIds.Contains(o.StopId) &&
                                       destStopIds.Contains(d.StopId) &&
                                       d.StopSequence > o.StopSequence &&
                                       previousDayServiceIds.Contains(t.ServiceId) &&
                                       o.DepartureSeconds >= previousDayMinDepartureSeconds
                                 select new DirectTripResult
                                 {
                                     TripId = t.TripId,
                                     RouteId = r.RouteId,
                                     RouteShortName = r.RouteShortName,
                                     OriginStopId = o.StopId,
                                     DestStopId = d.StopId,
                                     DepartureSeconds = o.DepartureSeconds.GetValueOrDefault(),
                                     ArrivalSeconds = d.ArrivalSeconds.GetValueOrDefault(),
                                     IsPreviousDayTrip = true
                                 };
            finalQuery = todayQuery.Concat(yesterdayQuery);
        }

        return await finalQuery.OrderBy(x => x.DepartureSeconds).Take(150).AsNoTracking().ToListAsync(cancellationToken);
    }

    private async Task<List<OneTransferResult>> FindOneTransferTripsAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<GtfsStop> allStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int transferBufferSeconds, int maxTransferWalkMeters, double walkingSpeed, CancellationToken cancellationToken)
    {
        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();
        int minDepartureSeconds = requestedSeconds + minWalkingTime;
        
        var todayLeg1Query = from o in _context.GtfsStopTimes
                               join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                               join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                               where originStopIds.Contains(o.StopId) &&
                                     activeServiceIds.Contains(t.ServiceId) &&
                                     o.DepartureSeconds >= minDepartureSeconds
                               select new Leg1TripData {
                                   TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName,
                                   OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), IsPreviousDayTrip = false
                               };

        IQueryable<Leg1TripData> finalLeg1Query = todayLeg1Query;

        if (previousDayServiceIds.Any())
        {
            int previousDayMinDepartureSeconds = requestedSeconds + 86400 + minWalkingTime;
            var yesterdayLeg1Query = from o in _context.GtfsStopTimes
                                       join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                                       join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                       where originStopIds.Contains(o.StopId) &&
                                             previousDayServiceIds.Contains(t.ServiceId) &&
                                             o.DepartureSeconds >= previousDayMinDepartureSeconds
                                       select new Leg1TripData {
                                           TripId = t.TripId, TripDbId = t.Id, RouteId = r.RouteId, RouteShortName = r.RouteShortName,
                                           OriginStopId = o.StopId, DepSeq = o.StopSequence, DepSecs = o.DepartureSeconds.GetValueOrDefault(), IsPreviousDayTrip = true
                                       };
            finalLeg1Query = todayLeg1Query.Concat(yesterdayLeg1Query);
        }

        var leg1Trips = await finalLeg1Query.OrderBy(x => x.DepSecs).Take(150).AsNoTracking().ToListAsync(cancellationToken);
        
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
            .Select(st => new { st.GtfsTripId, st.StopId, st.StopSequence, st.ArrivalSeconds })
            .AsNoTracking().ToListAsync(cancellationToken);

        var validLeg1Stops = new List<Leg1StopData>();
        foreach (var leg1 in leg1Trips)
        {
            var stopsAfter = leg1Stops.Where(s => s.GtfsTripId == leg1.TripDbId && s.StopSequence > leg1.DepSeq).ToList();
            foreach (var sa in stopsAfter)
            {
                validLeg1Stops.Add(new Leg1StopData { TripInfo = leg1, TransferStop1Id = sa.StopId, ArrSecs = sa.ArrivalSeconds.GetValueOrDefault() });
            }
        }

        var uniqueTransferStop1Ids = validLeg1Stops.Select(x => x.TransferStop1Id).Distinct().ToList();
        var transferStop1Entities = allStops.Where(s => uniqueTransferStop1Ids.Contains(s.StopId)).ToList();
        
        var transferPairs = new List<TransferPair>();
        foreach (var ts1 in transferStop1Entities)
        {
            foreach (var ts2 in allStops)
            {
                var dist = CalculateHaversineDistance(ts1.StopLat, ts1.StopLon, ts2.StopLat, ts2.StopLon);
                if (dist <= maxTransferWalkMeters)
                {
                    transferPairs.Add(new TransferPair { TransferStop1Id = ts1.StopId, TransferStop2Id = ts2.StopId, WalkSeconds = (int)(dist / walkingSpeed) });
                }
            }
        }

        var uniqueTransferStop2Ids = transferPairs.Select(x => x.TransferStop2Id).Distinct().ToList();
        var leg2Candidates = new List<Leg2TripData>();
        
        foreach (var chunk in uniqueTransferStop2Ids.Chunk(500))
        {
            var chunkIds = chunk.ToList();
            var todayLeg2Query = from ts in _context.GtfsStopTimes
                                   join d in _context.GtfsStopTimes on ts.GtfsTripId equals d.GtfsTripId
                                   join t in _context.GtfsTrips on ts.GtfsTripId equals t.Id
                                   join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                                   where chunkIds.Contains(ts.StopId) && destStopIds.Contains(d.StopId) && d.StopSequence > ts.StopSequence && activeServiceIds.Contains(t.ServiceId)
                                   select new Leg2TripData {
                                       TripId = t.TripId, RouteId = r.RouteId, RouteShortName = r.RouteShortName,
                                       TransferStop2Id = ts.StopId, DestStopId = d.StopId, DepSecs = ts.DepartureSeconds.GetValueOrDefault(), ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), IsPreviousDayTrip = false
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
                                           TripId = t.TripId, RouteId = r.RouteId, RouteShortName = r.RouteShortName,
                                           TransferStop2Id = ts.StopId, DestStopId = d.StopId, DepSecs = ts.DepartureSeconds.GetValueOrDefault(), ArrSecs = d.ArrivalSeconds.GetValueOrDefault(), IsPreviousDayTrip = true
                                       };
                finalLeg2Query = todayLeg2Query.Concat(yesterdayLeg2Query);
            }

            leg2Candidates.AddRange(await finalLeg2Query.AsNoTracking().ToListAsync(cancellationToken));
        }

        var results = new List<OneTransferResult>();
        var deduplicationSet = new HashSet<string>();

        foreach (var l1 in validLeg1Stops)
        {
            var pairs = transferPairs.Where(p => p.TransferStop1Id == l1.TransferStop1Id).ToList();
            foreach (var pair in pairs)
            {
                var l2s = leg2Candidates.Where(l2 => l2.TransferStop2Id == pair.TransferStop2Id).ToList();
                foreach (var l2 in l2s)
                {
                    if (l1.ArrSecs + pair.WalkSeconds + transferBufferSeconds <= l2.DepSecs)
                    {
                        var hash = $"{l1.TripInfo.TripId}_{l2.TripId}_{l1.TransferStop1Id}_{l2.TransferStop2Id}";
                        if (!deduplicationSet.Contains(hash))
                        {
                            deduplicationSet.Add(hash);
                            results.Add(new OneTransferResult
                            {
                                Leg1 = new LegData {
                                    TripId = l1.TripInfo.TripId, RouteId = l1.TripInfo.RouteId, RouteShortName = l1.TripInfo.RouteShortName,
                                    FromStopId = l1.TripInfo.OriginStopId, ToStopId = l1.TransferStop1Id, DepSecs = l1.TripInfo.DepSecs, ArrSecs = l1.ArrSecs, IsPreviousDayTrip = l1.TripInfo.IsPreviousDayTrip
                                },
                                Leg2 = new LegData {
                                    TripId = l2.TripId, RouteId = l2.RouteId, RouteShortName = l2.RouteShortName,
                                    FromStopId = l2.TransferStop2Id, ToStopId = l2.DestStopId, DepSecs = l2.DepSecs, ArrSecs = l2.ArrSecs, IsPreviousDayTrip = l2.IsPreviousDayTrip
                                },
                                TransferWalkMeters = pair.WalkSeconds > 0 ? (int)(pair.WalkSeconds * walkingSpeed) : 0,
                                TransferWalkSeconds = pair.WalkSeconds
                            });
                        }
                    }
                }
            }
        }

        return results.OrderBy(x => x.Leg2.ArrSecs).Take(50).ToList();
    }

    private List<StopWithDistance> FindStopsWithinRadius(List<GtfsStop> allStops, double lat, double lon, int maxMeters, double walkingSpeed, int maxCandidateStops)
    {
        var result = new List<StopWithDistance>();
        foreach (var stop in allStops)
        {
            var distance = CalculateHaversineDistance(lat, lon, stop.StopLat, stop.StopLon);
            if (distance <= maxMeters)
            {
                result.Add(new StopWithDistance 
                { 
                    Stop = stop, 
                    DistanceMeters = (int)distance,
                    WalkingTimeSeconds = (int)(distance / walkingSpeed)
                });
            }
        }
        return result.OrderBy(x => x.DistanceMeters).Take(maxCandidateStops).ToList();
    }

    private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var R = 6371e3; // metres
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    private class StopWithDistance
    {
        public GtfsStop Stop { get; set; } = null!;
        public int DistanceMeters { get; set; }
        public int WalkingTimeSeconds { get; set; }
    }

    private class DirectTripResult
    {
        public string TripId { get; set; } = null!;
        public string RouteId { get; set; } = null!;
        public string RouteShortName { get; set; } = null!;
        public string OriginStopId { get; set; } = null!;
        public string DestStopId { get; set; } = null!;
        public int DepartureSeconds { get; set; }
        public int ArrivalSeconds { get; set; }
        public bool IsPreviousDayTrip { get; set; }
    }

    private class Leg1TripData { public string TripId { get; set; } = null!; public int TripDbId { get; set; } public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public string OriginStopId { get; set; } = null!; public int DepSeq { get; set; } public int DepSecs { get; set; } public bool IsPreviousDayTrip { get; set; } }
    private class Leg1StopData { public Leg1TripData TripInfo { get; set; } = null!; public string TransferStop1Id { get; set; } = null!; public int ArrSecs { get; set; } }
    private class Leg2TripData { public string TripId { get; set; } = null!; public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public string TransferStop2Id { get; set; } = null!; public string DestStopId { get; set; } = null!; public int DepSecs { get; set; } public int ArrSecs { get; set; } public bool IsPreviousDayTrip { get; set; } }
    private class TransferPair { public string TransferStop1Id { get; set; } = null!; public string TransferStop2Id { get; set; } = null!; public int WalkSeconds { get; set; } }
    
    private class LegData { public string TripId { get; set; } = null!; public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public string FromStopId { get; set; } = null!; public string ToStopId { get; set; } = null!; public int DepSecs { get; set; } public int ArrSecs { get; set; } public bool IsPreviousDayTrip { get; set; } }
    private class OneTransferResult { public LegData Leg1 { get; set; } = null!; public LegData Leg2 { get; set; } = null!; public int TransferWalkMeters { get; set; } public int TransferWalkSeconds { get; set; } }
}
