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
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services;

public class JourneyPlanningService : IJourneyPlanningService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    private class ActiveStopsCache
    {
        public List<GtfsStop> Stops { get; set; } = new();
        public Dictionary<string, List<GtfsStop>> SpatialGrid { get; set; } = new();
        public Dictionary<string, List<GtfsTransfer>> TransfersByStopId { get; set; } = new();
        public Dictionary<int, List<int>> TripStopSequences { get; set; } = new();
    }

    private static string GetGridKey(double lat, double lon)
    {
        return $"{Math.Floor(lat / 0.01)}_{Math.Floor(lon / 0.01)}";
    }

    private static List<string> GetNeighborGridKeys(double lat, double lon)
    {
        var keys = new List<string>(9);
        int x = (int)Math.Floor(lat / 0.01);
        int y = (int)Math.Floor(lon / 0.01);
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                keys.Add($"{x + i}_{y + j}");
        return keys;
    }

    public JourneyPlanningService(AppDbContext context, IMemoryCache cache, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _context = context;
        _cache = cache;
        _configuration = configuration;
    }

    public async Task<JourneyPlanSearchResponse> SearchJourneyAsync(JourneyPlanSearchRequest request, CancellationToken cancellationToken = default)
    {
        // 0. Check for Active Feed
        var activeRun = await _context.GtfsImportRuns
            .AsNoTracking()
            .Where(r => r.IsActive && r.Status == "Completed")
            .FirstOrDefaultAsync(cancellationToken);

        if (activeRun == null)
        {
            throw new ulasim_veri_servisi.Exceptions.ActiveFeedNotFoundException();
        }

        // 0.1 Load configurations earlier for cache key isolation
        int configMaxWalkingMeters = _configuration.GetValue<int>("JourneyPlan:MaxWalkingMeters", 1500);
        int finalMaxWalkingMeters = Math.Min(request.MaxWalkingMeters, configMaxWalkingMeters);
        double walkingSpeed = _configuration.GetValue<double>("JourneyPlan:WalkingSpeed", 1.4);
        int maxCandidateStops = _configuration.GetValue<int>("JourneyPlan:MaxCandidateStops", 5);
        int transferBufferMinutes = _configuration.GetValue<int>("JourneyPlan:TransferBufferMinutes", 3);
        int maxTransferWalkMeters = _configuration.GetValue<int>("JourneyPlan:MaxTransferWalkMeters", 500);

        var utcTimeKey = request.DepartureDateTime!.Value.ToUniversalTime().ToString("yyyyMMdd_HHmm");
        string cacheKey = $"JourneyPlan_{request.Origin.Lat}_{request.Origin.Lon}_{request.Destination.Lat}_{request.Destination.Lon}_{utcTimeKey}_{request.MaxTransfers}_{finalMaxWalkingMeters}_{request.MaxResults}_{walkingSpeed}_{maxCandidateStops}_{transferBufferMinutes}_{maxTransferWalkMeters}_{activeRun.Id}";

        if (_cache.TryGetValue(cacheKey, out JourneyPlanSearchResponse? cachedResponse) && cachedResponse != null)
        {
            return cachedResponse;
        }

        var response = new JourneyPlanSearchResponse();

        // Fetch agency info for timezone
        var agency = await _context.GtfsAgencies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        string timezone = agency?.AgencyTimezone ?? "Europe/Istanbul";

        // Fetch boundaries from calendars
        var minDate = await _context.GtfsCalendars.AsNoTracking().MinAsync(c => (DateOnly?)c.StartDate, cancellationToken);
        var maxDate = await _context.GtfsCalendars.AsNoTracking().MaxAsync(c => (DateOnly?)c.EndDate, cancellationToken);

        response.Metadata = new JourneyPlanMetadataDto
        {
            ActiveImportId = activeRun.Id,
            FeedHash = activeRun.FileHash ?? "UNKNOWN",
            Timezone = timezone,
            StartDate = minDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            EndDate = maxDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            IsStale = maxDate.HasValue && maxDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
        };

        // 1. Get Active Stops from Cache or DB
        var activeStopsCache = await GetActiveStopsAsync(activeRun.Id.ToString(), cancellationToken);
        var activeStops = activeStopsCache.Stops;
        if (!activeStops.Any()) 
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }


        // 3. Find Origin and Destination Stops within walking distance
        var originStops = FindStopsWithinRadius(activeStops, request.Origin.Lat, request.Origin.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);
        var destStops = FindStopsWithinRadius(activeStops, request.Destination.Lat, request.Destination.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);

        if (!originStops.Any() || !destStops.Any())
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }

        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();

        // 4. Resolve Target Date and Time in Local Timezone
        TimeZoneInfo tzi;
        try { tzi = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { tzi = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time"); }

        var localDateTime = TimeZoneInfo.ConvertTime(request.DepartureDateTime!.Value, tzi);
        var targetDate = localDateTime.Date;
        var requestedSeconds = (int)localDateTime.TimeOfDay.TotalSeconds;

        var activeServiceIds = await GetActiveServiceIdsAsync(targetDate, cancellationToken);
        var previousDayServiceIds = new List<string>();

        // If requested time is early morning, consider previous day's trips that go past midnight (24:00+)
        if (requestedSeconds < 4 * 3600)
        {
            previousDayServiceIds = await GetActiveServiceIdsAsync(targetDate.AddDays(-1), cancellationToken);
        }

        if (!activeServiceIds.Any() && !previousDayServiceIds.Any()) 
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }

        var itineraries = new List<ItineraryDto>();

        // 5. Find Direct Routes (0-Transfer)
        int minWalkingTime = originStops.Min(x => x.WalkingTimeSeconds);
        int maxWaitTimeMinutes = _configuration.GetValue<int>("JourneyPlan:MaxWaitTimeMinutes", 60);
        int maxJourneyTimeMinutes = _configuration.GetValue<int>("JourneyPlan:MaxJourneyTimeMinutes", 240);
        int maxDirectTrips = _configuration.GetValue<int>("JourneyPlan:MaxDirectTrips", 500);
        var directTrips = await FindDirectTripsAsync(originStopIds, destStopIds, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, maxJourneyTimeMinutes, cancellationToken);
        
        // Exact walking time filter in memory
        var validTrips = directTrips.Where(trip => 
        {
            var oStop = originStops.First(x => x.Stop.StopId == trip.OriginStopId);
            int baseReqSecs = trip.IsPreviousDayTrip ? requestedSeconds + 86400 : requestedSeconds;
            return trip.DepartureSeconds >= baseReqSecs + oStop.WalkingTimeSeconds;
        }).OrderBy(x => x.IsPreviousDayTrip ? x.ArrivalSeconds - 86400 : x.ArrivalSeconds).ToList();

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
                ToStopName = "Varış Noktası",
                DepartureTime = leg.ArrivalTime,
                ArrivalTime = leg.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                DistanceMeters = dStop.DistanceMeters,
                DurationMinutes = dStop.WalkingTimeSeconds / 60
            };

            var serviceDate = trip.IsPreviousDayTrip ? targetDate.AddDays(-1).ToString("yyyy-MM-dd") : targetDate.ToString("yyyy-MM-dd");
            itineraries.Add(CreateItineraryDto(request, new List<LegDto> { walk1, leg, walk2 }, serviceDate));
        }

        // 6. If 1-Transfer is requested, implement 1-transfer logic
        if (request.MaxTransfers >= 1)
        {
            int maxLegTrips = _configuration.GetValue<int>("JourneyPlan:MaxLegTrips", 500);
            int maxTransferTrips = _configuration.GetValue<int>("JourneyPlan:MaxTransferTrips", 150);
            
            var transferTrips = await FindOneTransferTripsAsync(originStops, destStops, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, transferBufferMinutes * 60, targetDate, tzi, activeStopsCache, maxTransferWalkMeters, walkingSpeed, maxLegTrips, maxTransferTrips, maxWaitTimeMinutes, maxJourneyTimeMinutes, cancellationToken);
            
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

                var walk1 = new LegDto
                {
                    Mode = "WALK",
                    FromStopId = "ORIGIN",
                    FromStopName = "Mevcut Konum",
                    ToStopId = oStop.Stop.StopId,
                    ToStopName = oStop.Stop.StopName,
                    DepartureTime = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1)).AddSeconds(-oStop.WalkingTimeSeconds),
                    ArrivalTime = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1)),
                    DistanceMeters = oStop.DistanceMeters,
                    DurationMinutes = oStop.WalkingTimeSeconds / 60
                };
                
                var leg1 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg1.RouteId, RouteShortName = tResult.Leg1.RouteShortName, RouteType = tResult.Leg1.RouteType, TripId = tResult.Leg1.TripId, Headsign = tResult.Leg1.Headsign, DirectionId = tResult.Leg1.DirectionId,
                    PatternId = tResult.Leg1.PatternId, ShapeId = tResult.Leg1.ShapeId, ServiceId = tResult.Leg1.ServiceId, ServiceDate = tResult.Leg1.ServiceDate,
                    FromStopId = tResult.Leg1.FromStopId, FromStopName = oStop.Stop.StopName, FromStopSequence = tResult.Leg1.FromStopSequence,
                    ToStopId = tResult.Leg1.ToStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg1.ToStopId).StopName, ToStopSequence = tResult.Leg1.ToStopSequence,
                    DepartureTime = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1)),
                    RawGtfsDepartureTime = tResult.Leg1.DepTimeRaw,
                    RawGtfsDepartureSeconds = tResult.Leg1.DepSecs,
                    ArrivalTime = new DateTimeOffset(arrDt1, tzi.GetUtcOffset(arrDt1)),
                    RawGtfsArrivalTime = tResult.Leg1.ArrTimeRaw,
                    RawGtfsArrivalSeconds = tResult.Leg1.ArrSecs,
                    DurationMinutes = (tResult.Leg1.ArrSecs - tResult.Leg1.DepSecs) / 60,
                    StopCount = tResult.Leg1.StopCount,
                    IntermediateStopCount = tResult.Leg1.StopCount - 1 > 0 ? tResult.Leg1.StopCount - 1 : 0
                };
                
                var walkTransfer = new LegDto
                {
                    Mode = "WALK",
                    FromStopId = leg1.ToStopId,
                    FromStopName = leg1.ToStopName,
                    ToStopId = tResult.Leg2.FromStopId,
                    ToStopName = activeStops.First(s => s.StopId == tResult.Leg2.FromStopId).StopName,
                    DepartureTime = leg1.ArrivalTime,
                    ArrivalTime = leg1.ArrivalTime.Value.AddSeconds(tResult.TransferWalkSeconds),
                    DistanceMeters = tResult.TransferWalkMeters,
                    DurationMinutes = tResult.TransferWalkSeconds / 60
                };

                var leg2 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg2.RouteId, RouteShortName = tResult.Leg2.RouteShortName, RouteType = tResult.Leg2.RouteType, TripId = tResult.Leg2.TripId, Headsign = tResult.Leg2.Headsign, DirectionId = tResult.Leg2.DirectionId,
                    PatternId = tResult.Leg2.PatternId, ShapeId = tResult.Leg2.ShapeId, ServiceId = tResult.Leg2.ServiceId, ServiceDate = tResult.Leg2.ServiceDate,
                    FromStopId = tResult.Leg2.FromStopId, FromStopName = walkTransfer.ToStopName, FromStopSequence = tResult.Leg2.FromStopSequence,
                    ToStopId = tResult.Leg2.ToStopId, ToStopName = dStop.Stop.StopName, ToStopSequence = tResult.Leg2.ToStopSequence,
                    DepartureTime = new DateTimeOffset(depDt2, tzi.GetUtcOffset(depDt2)),
                    RawGtfsDepartureTime = tResult.Leg2.DepTimeRaw,
                    RawGtfsDepartureSeconds = tResult.Leg2.DepSecs,
                    ArrivalTime = new DateTimeOffset(arrDt2, tzi.GetUtcOffset(arrDt2)),
                    RawGtfsArrivalTime = tResult.Leg2.ArrTimeRaw,
                    RawGtfsArrivalSeconds = tResult.Leg2.ArrSecs,
                    DurationMinutes = (tResult.Leg2.ArrSecs - tResult.Leg2.DepSecs) / 60,
                    StopCount = tResult.Leg2.StopCount,
                    IntermediateStopCount = tResult.Leg2.StopCount - 1 > 0 ? tResult.Leg2.StopCount - 1 : 0
                };
                
                var walk2 = new LegDto
                {
                    Mode = "WALK",
                    FromStopId = dStop.Stop.StopId,
                    FromStopName = dStop.Stop.StopName,
                    ToStopId = "DEST",
                    ToStopName = "Varış Noktası",
                    DepartureTime = leg2.ArrivalTime,
                    ArrivalTime = leg2.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                    DistanceMeters = dStop.DistanceMeters,
                    DurationMinutes = dStop.WalkingTimeSeconds / 60
                };

                var serviceDate = tResult.Leg1.IsPreviousDayTrip ? targetDate.AddDays(-1).ToString("yyyy-MM-dd") : targetDate.ToString("yyyy-MM-dd");
                itineraries.Add(CreateItineraryDto(request, new List<LegDto> { walk1, leg1, walkTransfer, leg2, walk2 }, serviceDate));
            }
        }

        // 7. If 2-Transfers is requested, implement 2-transfer logic
        if (request.MaxTransfers >= 2)
        {
            int maxLegTrips = _configuration.GetValue<int>("JourneyPlan:MaxLegTrips", 500);
            int maxTwoTransferTrips = _configuration.GetValue<int>("JourneyPlan:MaxTwoTransferTrips", 50);
            
            var twoTransferTrips = await FindTwoTransferTripsAsync(originStops, destStops, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, transferBufferMinutes * 60, targetDate, tzi, activeStopsCache, maxTransferWalkMeters, walkingSpeed, maxLegTrips, maxTwoTransferTrips, maxWaitTimeMinutes, maxJourneyTimeMinutes, cancellationToken);
            
            foreach (var tResult in twoTransferTrips.Take(request.MaxResults - itineraries.Count))
            {
                var oStop = originStops.First(x => x.Stop.StopId == tResult.Leg1.FromStopId);
                var dStop = destStops.First(x => x.Stop.StopId == tResult.Leg3.ToStopId);
                
                DateTime baseDate1 = tResult.Leg1.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                DateTime depDt1 = baseDate1.AddSeconds(tResult.Leg1.DepSecs);
                DateTime arrDt1 = baseDate1.AddSeconds(tResult.Leg1.ArrSecs);
                
                DateTime baseDate2 = tResult.Leg2.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                DateTime depDt2 = baseDate2.AddSeconds(tResult.Leg2.DepSecs);
                DateTime arrDt2 = baseDate2.AddSeconds(tResult.Leg2.ArrSecs);

                DateTime baseDate3 = tResult.Leg3.IsPreviousDayTrip ? targetDate.AddDays(-1) : targetDate;
                DateTime depDt3 = baseDate3.AddSeconds(tResult.Leg3.DepSecs);
                DateTime arrDt3 = baseDate3.AddSeconds(tResult.Leg3.ArrSecs);

                var walk1 = new LegDto
                {
                    Mode = "WALK", FromStopId = "ORIGIN", FromStopName = "Mevcut Konum", ToStopId = oStop.Stop.StopId, ToStopName = oStop.Stop.StopName,
                    DepartureTime = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1)).AddSeconds(-oStop.WalkingTimeSeconds),
                    ArrivalTime = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1)),
                    DistanceMeters = oStop.DistanceMeters, DurationMinutes = oStop.WalkingTimeSeconds / 60
                };
                
                var leg1 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg1.RouteId, RouteShortName = tResult.Leg1.RouteShortName, RouteType = tResult.Leg1.RouteType, TripId = tResult.Leg1.TripId, Headsign = tResult.Leg1.Headsign, DirectionId = tResult.Leg1.DirectionId,
                    PatternId = tResult.Leg1.PatternId, ShapeId = tResult.Leg1.ShapeId, ServiceId = tResult.Leg1.ServiceId, ServiceDate = tResult.Leg1.ServiceDate,
                    FromStopId = tResult.Leg1.FromStopId, FromStopName = oStop.Stop.StopName, FromStopSequence = tResult.Leg1.FromStopSequence,
                    ToStopId = tResult.Leg1.ToStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg1.ToStopId).StopName, ToStopSequence = tResult.Leg1.ToStopSequence,
                    DepartureTime = new DateTimeOffset(depDt1, tzi.GetUtcOffset(depDt1)), RawGtfsDepartureTime = tResult.Leg1.DepTimeRaw, RawGtfsDepartureSeconds = tResult.Leg1.DepSecs,
                    ArrivalTime = new DateTimeOffset(arrDt1, tzi.GetUtcOffset(arrDt1)), RawGtfsArrivalTime = tResult.Leg1.ArrTimeRaw, RawGtfsArrivalSeconds = tResult.Leg1.ArrSecs,
                    DurationMinutes = (tResult.Leg1.ArrSecs - tResult.Leg1.DepSecs) / 60, StopCount = tResult.Leg1.StopCount, IntermediateStopCount = tResult.Leg1.StopCount - 1 > 0 ? tResult.Leg1.StopCount - 1 : 0
                };
                
                var walkTransfer1 = new LegDto
                {
                    Mode = "WALK", FromStopId = leg1.ToStopId, FromStopName = leg1.ToStopName, ToStopId = tResult.Leg2.FromStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg2.FromStopId).StopName,
                    DepartureTime = leg1.ArrivalTime, ArrivalTime = leg1.ArrivalTime.Value.AddSeconds(tResult.TransferWalk1Seconds), DistanceMeters = tResult.TransferWalk1Meters, DurationMinutes = tResult.TransferWalk1Seconds / 60
                };

                var leg2 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg2.RouteId, RouteShortName = tResult.Leg2.RouteShortName, RouteType = tResult.Leg2.RouteType, TripId = tResult.Leg2.TripId, Headsign = tResult.Leg2.Headsign, DirectionId = tResult.Leg2.DirectionId,
                    PatternId = tResult.Leg2.PatternId, ShapeId = tResult.Leg2.ShapeId, ServiceId = tResult.Leg2.ServiceId, ServiceDate = tResult.Leg2.ServiceDate,
                    FromStopId = tResult.Leg2.FromStopId, FromStopName = walkTransfer1.ToStopName, FromStopSequence = tResult.Leg2.FromStopSequence,
                    ToStopId = tResult.Leg2.ToStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg2.ToStopId).StopName, ToStopSequence = tResult.Leg2.ToStopSequence,
                    DepartureTime = new DateTimeOffset(depDt2, tzi.GetUtcOffset(depDt2)), RawGtfsDepartureTime = tResult.Leg2.DepTimeRaw, RawGtfsDepartureSeconds = tResult.Leg2.DepSecs,
                    ArrivalTime = new DateTimeOffset(arrDt2, tzi.GetUtcOffset(arrDt2)), RawGtfsArrivalTime = tResult.Leg2.ArrTimeRaw, RawGtfsArrivalSeconds = tResult.Leg2.ArrSecs,
                    DurationMinutes = (tResult.Leg2.ArrSecs - tResult.Leg2.DepSecs) / 60, StopCount = tResult.Leg2.StopCount, IntermediateStopCount = tResult.Leg2.StopCount - 1 > 0 ? tResult.Leg2.StopCount - 1 : 0
                };

                var walkTransfer2 = new LegDto
                {
                    Mode = "WALK", FromStopId = leg2.ToStopId, FromStopName = leg2.ToStopName, ToStopId = tResult.Leg3.FromStopId, ToStopName = activeStops.First(s => s.StopId == tResult.Leg3.FromStopId).StopName,
                    DepartureTime = leg2.ArrivalTime, ArrivalTime = leg2.ArrivalTime.Value.AddSeconds(tResult.TransferWalk2Seconds), DistanceMeters = tResult.TransferWalk2Meters, DurationMinutes = tResult.TransferWalk2Seconds / 60
                };

                var leg3 = new LegDto
                {
                    Mode = "TRANSIT", RouteId = tResult.Leg3.RouteId, RouteShortName = tResult.Leg3.RouteShortName, RouteType = tResult.Leg3.RouteType, TripId = tResult.Leg3.TripId, Headsign = tResult.Leg3.Headsign, DirectionId = tResult.Leg3.DirectionId,
                    PatternId = tResult.Leg3.PatternId, ShapeId = tResult.Leg3.ShapeId, ServiceId = tResult.Leg3.ServiceId, ServiceDate = tResult.Leg3.ServiceDate,
                    FromStopId = tResult.Leg3.FromStopId, FromStopName = walkTransfer2.ToStopName, FromStopSequence = tResult.Leg3.FromStopSequence,
                    ToStopId = tResult.Leg3.ToStopId, ToStopName = dStop.Stop.StopName, ToStopSequence = tResult.Leg3.ToStopSequence,
                    DepartureTime = new DateTimeOffset(depDt3, tzi.GetUtcOffset(depDt3)), RawGtfsDepartureTime = tResult.Leg3.DepTimeRaw, RawGtfsDepartureSeconds = tResult.Leg3.DepSecs,
                    ArrivalTime = new DateTimeOffset(arrDt3, tzi.GetUtcOffset(arrDt3)), RawGtfsArrivalTime = tResult.Leg3.ArrTimeRaw, RawGtfsArrivalSeconds = tResult.Leg3.ArrSecs,
                    DurationMinutes = (tResult.Leg3.ArrSecs - tResult.Leg3.DepSecs) / 60, StopCount = tResult.Leg3.StopCount, IntermediateStopCount = tResult.Leg3.StopCount - 1 > 0 ? tResult.Leg3.StopCount - 1 : 0
                };
                
                var walk2 = new LegDto
                {
                    Mode = "WALK", FromStopId = dStop.Stop.StopId, FromStopName = dStop.Stop.StopName, ToStopId = "DEST", ToStopName = "Varış Noktası",
                    DepartureTime = leg3.ArrivalTime, ArrivalTime = leg3.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                    DistanceMeters = dStop.DistanceMeters, DurationMinutes = dStop.WalkingTimeSeconds / 60
                };

                var serviceDate = tResult.Leg1.IsPreviousDayTrip ? targetDate.AddDays(-1).ToString("yyyy-MM-dd") : targetDate.ToString("yyyy-MM-dd");
                itineraries.Add(CreateItineraryDto(request, new List<LegDto> { walk1, leg1, walkTransfer1, leg2, walkTransfer2, leg3, walk2 }, serviceDate));
            }
        }

        response.Itineraries = itineraries
            .OrderBy(x => x.ArrivalTime)
            .ThenBy(x => x.TransferCount)
            .ThenBy(x => x.TotalWalkingDistanceMeters)
            .ThenBy(x => x.TotalDurationMinutes)
            .ThenBy(x => x.TotalTransitStopCount)
            .ThenBy(x => string.Join("_", x.Legs.Select(l => l.TripId)))
            .Take(request.MaxResults)
            .ToList();
            
        if (request.IncludeIntermediateStops && response.Itineraries.Any())
        {
            await PopulateIntermediateStopsAsync(response.Itineraries, tzi, activeRun.Id, cancellationToken);
        }

        if (response.Itineraries.Any())
        {
            response.ReasonCode = "SUCCESS";
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                Size = 1
            };
            _cache.Set(cacheKey, response, cacheOptions);
        }
        else
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
        }

        return response;
    }

    private async Task<ActiveStopsCache> GetActiveStopsAsync(string activeRunId, CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync($"ActiveGtfsStops_{activeRunId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            entry.Size = 1; // Required if MemoryCache has a SizeLimit configured
            var stops = await _context.GtfsStops.AsNoTracking().ToListAsync(cancellationToken);
            
            var grid = new Dictionary<string, List<GtfsStop>>();
            foreach (var s in stops)
            {
                var key = GetGridKey(s.StopLat, s.StopLon);
                if (!grid.TryGetValue(key, out var list))
                {
                    list = new List<GtfsStop>();
                    grid[key] = list;
                }
                list.Add(s);
            }
            var transfers = await _context.GtfsTransfers.AsNoTracking().ToListAsync(cancellationToken);
            var transfersDict = transfers.GroupBy(t => t.FromStopId).ToDictionary(g => g.Key, g => g.ToList());
            
            var stopTimes = await _context.GtfsStopTimes
                .Select(st => new { st.GtfsTripId, st.StopSequence })
                .AsNoTracking()
                .ToListAsync(cancellationToken);
                
            var tripSequences = stopTimes
                .GroupBy(st => st.GtfsTripId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.StopSequence).Select(x => x.StopSequence).ToList());
            
            return new ActiveStopsCache { Stops = stops, SpatialGrid = grid, TransfersByStopId = transfersDict, TripStopSequences = tripSequences };
        }) ?? new ActiveStopsCache();
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

    private async Task<List<DirectTripResult>> FindDirectTripsAsync(List<string> originStopIds, List<string> destStopIds, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int maxJourneyTimeMinutes, CancellationToken cancellationToken)
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

    private async Task<List<OneTransferResult>> FindOneTransferTripsAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int transferBufferSeconds, DateTime targetDate, TimeZoneInfo tzi, ActiveStopsCache activeStopsCache, int maxTransferWalkMeters, double walkingSpeed, int maxLegTrips, int maxTransferTrips, int maxWaitTimeMinutes, int maxJourneyTimeMinutes, CancellationToken cancellationToken)
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
            foreach(var leg2 in chunkResults)
            {
                if (activeStopsCache.TripStopSequences.TryGetValue(leg2.TripDbId, out var seqs))
                {
                    leg2.StopCount = seqs.Count(s => s > leg2.DepSeq && s <= leg2.ArrSeq);
                }
            }
            leg2Candidates.AddRange(chunkResults);
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
            System.IO.File.AppendAllText("dedup-debug.txt", $"[DEBUG-DEDUP] Leg1={res.Leg1.TripId}, Leg2={res.Leg2.TripId}, Hash={hash}\n");
            
            if (patternHashes.Add(hash))
            {
                deduplicatedResults.Add(res);
            }
            else 
            {
                System.IO.File.AppendAllText("dedup-debug.txt", $"[DEBUG-DEDUP] REJECTED Leg1={res.Leg1.TripId}, Leg2={res.Leg2.TripId} due to duplicate hash\n");
            }
        }
        System.IO.File.AppendAllText("dedup-debug.txt", $"[DEBUG-DEDUP] Total before={results.Count}, after={deduplicatedResults.Count}\n");
        return deduplicatedResults;
    }
    private async Task<List<TwoTransferResult>> FindTwoTransferTripsAsync(List<StopWithDistance> originStops, List<StopWithDistance> destStops, List<string> activeServiceIds, List<string> previousDayServiceIds, int requestedSeconds, int minWalkingTime, int transferBufferSeconds, DateTime targetDate, TimeZoneInfo tzi, ActiveStopsCache activeStopsCache, int maxTransferWalkMeters, double walkingSpeed, int maxLegTrips, int maxTwoTransferTrips, int maxWaitTimeMinutes, int maxJourneyTimeMinutes, CancellationToken cancellationToken)
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
        // We need to reverse loop transfers, or just loop through all transfers that end at uniqueTransferStop2Ids
        // To be safe and fast, loop through activeStopsCache.TransfersByStopId
        foreach (var kvp in activeStopsCache.TransfersByStopId)
        {
            var originStopId = kvp.Key;
            foreach (var tr in kvp.Value.Where(tr => tr.DistanceMeters <= maxTransferWalkMeters))
            {
                if (uniqueTransferStop2Ids.Contains(tr.ToStopId))
                {
                    transfer2Pairs.Add(new TransferPair { TransferStop1Id = originStopId, TransferStop2Id = tr.ToStopId, WalkSeconds = tr.WalkingTimeSeconds });
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
                foreach(var leg2 in chunkResults)
                {
                    if (activeStopsCache.TripStopSequences.TryGetValue(leg2.TripDbId, out var seqs))
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

        foreach (var l1 in validLeg1Stops)
        {
            var p1List = transfer1Pairs.Where(p => p.TransferStop1Id == l1.TransferStop1Id).ToList();
            foreach (var p1 in p1List)
            {
                var l2List = leg2Candidates.Where(l2 => l2.TransferStop2Id == p1.TransferStop2Id).ToList();
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

                        var p2List = transfer2Pairs.Where(p => p.TransferStop1Id == l2.DestStopId).ToList();
                        foreach (var p2 in p2List)
                        {
                            var l3List = validLeg3Stops.Where(l3 => l3.TransferStop2Id == p2.TransferStop2Id).ToList();
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

    private async Task PopulateIntermediateStopsAsync(List<ItineraryDto> itineraries, TimeZoneInfo tzi, int importId, CancellationToken cancellationToken)
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
            .ToListAsync(cancellationToken);

        foreach (var leg in transitLegs)
        {
            if (leg.TripId == null || leg.FromStopSequence == null || leg.ToStopSequence == null || leg.ServiceDate == null) 
                continue;

            var baseDate = DateTime.Parse(leg.ServiceDate);
            var intermediates = stopTimes
                .Where(st => st.TripId == leg.TripId && 
                             st.StopSequence > leg.FromStopSequence.Value && 
                             st.StopSequence < leg.ToStopSequence.Value)
                .OrderBy(st => st.StopSequence)
                .Select(st => 
                {
                    DateTimeOffset? arrTime = null;
                    if (st.ArrivalSeconds.HasValue)
                    {
                        var arrDt = baseDate.AddSeconds(st.ArrivalSeconds.Value);
                        arrTime = new DateTimeOffset(arrDt, tzi.GetUtcOffset(arrDt));
                    }

                    return new IntermediateStopDto
                    {
                        StopId = st.Stop.StopId,
                        StopCode = st.Stop.StopCode,
                        StopName = st.Stop.StopName,
                        StopSequence = st.StopSequence,
                        RawGtfsArrivalTime = st.ArrivalTimeRaw,
                        RawGtfsDepartureTime = st.DepartureTimeRaw,
                        ArrivalSeconds = st.ArrivalSeconds,
                        DepartureSeconds = st.DepartureSeconds,
                        ArrivalTime = arrTime,
                        Lat = st.Stop.StopLat,
                        Lon = st.Stop.StopLon
                    };
                }).ToList();

            leg.IntermediateStops = intermediates;
        }
    }

    private class DirectTripResult
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

    private class Leg1TripData { public string TripId { get; set; } = null!; public int TripDbId { get; set; } public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? TripHeadsign { get; set; } public int? DirectionId { get; set; } public string OriginStopId { get; set; } = null!; public int DepSeq { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } }
    private class Leg1StopData { public Leg1TripData TripInfo { get; set; } = null!; public string TransferStop1Id { get; set; } = null!; public int ArrSeq { get; set; } public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public int StopCount { get; set; } }
    private class Leg2TripData { public string TripId { get; set; } = null!; public int TripDbId { get; set; } public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? TripHeadsign { get; set; } public int? DirectionId { get; set; } public string TransferStop2Id { get; set; } = null!; public string DestStopId { get; set; } = null!; public int DepSeq { get; set; } public int ArrSeq { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public int StopCount { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } }
    private class TransferPair { public string TransferStop1Id { get; set; } = null!; public string TransferStop2Id { get; set; } = null!; public int WalkSeconds { get; set; } }
    
    private class LegData { public string TripId { get; set; } = null!; public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? Headsign { get; set; } public int? DirectionId { get; set; } public string FromStopId { get; set; } = null!; public string ToStopId { get; set; } = null!; public int FromStopSequence { get; set; } public int ToStopSequence { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public int StopCount { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } public string ServiceDate { get; set; } = null!; public string PatternId { get; set; } = null!; }
    private class OneTransferResult { public LegData Leg1 { get; set; } = null!; public LegData Leg2 { get; set; } = null!; public int TransferWalkMeters { get; set; } public int TransferWalkSeconds { get; set; } }
    
    private class Leg3TripData { public string TripId { get; set; } = null!; public int TripDbId { get; set; } public string RouteId { get; set; } = null!; public string RouteShortName { get; set; } = null!; public int? RouteType { get; set; } public string? TripHeadsign { get; set; } public int? DirectionId { get; set; } public string DestStopId { get; set; } = null!; public int ArrSeq { get; set; } public int ArrSecs { get; set; } public string ArrTimeRaw { get; set; } = null!; public bool IsPreviousDayTrip { get; set; } public string ServiceId { get; set; } = null!; public string? ShapeId { get; set; } }
    private class Leg3StopData { public Leg3TripData TripInfo { get; set; } = null!; public string TransferStop2Id { get; set; } = null!; public int DepSeq { get; set; } public int DepSecs { get; set; } public string DepTimeRaw { get; set; } = null!; public int StopCount { get; set; } }
    private class TwoTransferResult { public LegData Leg1 { get; set; } = null!; public LegData Leg2 { get; set; } = null!; public LegData Leg3 { get; set; } = null!; public int TransferWalk1Meters { get; set; } public int TransferWalk1Seconds { get; set; } public int TransferWalk2Meters { get; set; } public int TransferWalk2Seconds { get; set; } }

    private ItineraryDto CreateItineraryDto(JourneyPlanSearchRequest request, List<LegDto> legs, string serviceDate)
    {
        var transitLegs = legs.Where(l => l.Mode == "TRANSIT").ToList();
        var walkLegs = legs.Where(l => l.Mode == "WALK").ToList();

        var departureTime = legs.First().DepartureTime.Value;
        var arrivalTime = legs.Last().ArrivalTime.Value;

        var initialWait = (int)(departureTime - request.DepartureDateTime).GetValueOrDefault().TotalSeconds;
        if (initialWait < 0) initialWait = 0;

        var transferWaitTimes = new List<int>();
        for (int i = 0; i < transitLegs.Count - 1; i++)
        {
            var currentTransit = transitLegs[i];
            var nextTransit = transitLegs[i + 1];
            
            var walkBetween = legs.FirstOrDefault(l => l.Mode == "WALK" && l.DepartureTime == currentTransit.ArrivalTime);
            var arrivedAtNextStop = walkBetween != null ? walkBetween.ArrivalTime.Value : currentTransit.ArrivalTime.Value;
            
            var wait = (int)(nextTransit.DepartureTime.Value - arrivedAtNextStop).TotalSeconds;
            transferWaitTimes.Add(wait < 0 ? 0 : wait);
        }

        var totalWait = initialWait + transferWaitTimes.Sum();
        var totalWalkDistance = walkLegs.Sum(l => l.DistanceMeters);
        var totalWalkTime = walkLegs.Sum(l => (int)(l.ArrivalTime.Value - l.DepartureTime.Value).TotalSeconds);
        var totalInVehicleTime = transitLegs.Sum(l => (int)(l.ArrivalTime.Value - l.DepartureTime.Value).TotalSeconds);
        var totalTransitStops = transitLegs.Sum(l => l.StopCount);

        var routeTypes = transitLegs.Where(l => l.RouteType.HasValue).Select(l => l.RouteType.Value).Distinct().Select(rt => 
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
}
