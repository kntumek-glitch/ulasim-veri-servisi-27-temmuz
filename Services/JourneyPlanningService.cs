using ulasim_veri_servisi.Services.JourneyPlanning.Models;
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

public partial class JourneyPlanningService : IJourneyPlanningService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<JourneyPlanningService> _logger;
    private readonly ulasim_veri_servisi.Services.JourneyPlanCacheTokenSource _cacheTokenSource;
    private readonly WalkingRoutingService _walkingRoutingService;
    private readonly ulasim_veri_servisi.Services.JourneyPlanning.DataAccess.IJourneyCacheService _cacheService;
    private readonly ulasim_veri_servisi.Services.JourneyPlanning.Spatial.ISpatialCalculatorService _spatialService;
    private readonly ulasim_veri_servisi.Services.JourneyPlanning.Algorithms.IJourneyRoutingEngine _routingEngine;
    private readonly ulasim_veri_servisi.Services.JourneyPlanning.Mapping.IJourneyResultMapper _mapper;


    public JourneyPlanningService(
        AppDbContext context,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<JourneyPlanningService> logger,
        ulasim_veri_servisi.Services.JourneyPlanCacheTokenSource cacheTokenSource,
        WalkingRoutingService walkingRoutingService,
        ulasim_veri_servisi.Services.JourneyPlanning.DataAccess.IJourneyCacheService cacheService,
        ulasim_veri_servisi.Services.JourneyPlanning.Spatial.ISpatialCalculatorService spatialService,
        ulasim_veri_servisi.Services.JourneyPlanning.Algorithms.IJourneyRoutingEngine routingEngine,
        ulasim_veri_servisi.Services.JourneyPlanning.Mapping.IJourneyResultMapper mapper)
    {
        _context = context;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
        _cacheTokenSource = cacheTokenSource;
        _walkingRoutingService = walkingRoutingService;
        _cacheService = cacheService;
        _spatialService = spatialService;
        _routingEngine = routingEngine;
        _mapper = mapper;
    }

    public async Task<JourneyPlanSearchResponse> SearchJourneyV2Async(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SearchMode == RoutingMode.ARRIVE_BY)
        {
            return await SearchJourneyArriveByAsync(request, cancellationToken);
        }

        // Phase 1: DEPART_AT mode (maps to existing forward-search logic)
        var v1Request = new JourneyPlanSearchRequest
        {
            Origin = request.Origin,
            Destination = request.Destination,
            DepartureDateTime = request.DateTime,
            MaxTransfers = request.MaxTransfers,
            MaxWalkingMeters = request.MaxWalkingMeters,
            MaxResults = request.MaxResults,
            IncludeIntermediateStops = request.IncludeIntermediateStops,
            IncludeWalkingGeometry = request.IncludeWalkingGeometry
        };

        return await SearchJourneyAsync(v1Request, cancellationToken);
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
            throw new ulasim_veri_servisi.Exceptions.ActiveFeedNotFoundException("Sistemde iÅŸlem yapabilecek aktif bir GTFS veri seti bulunamadÄ±.");
        }

        // 0.1 Load configurations earlier for cache key isolation
        int configMaxWalkingMeters = _configuration.GetValue<int>("JourneyPlan:MaxWalkingMeters", 1500);
        int finalMaxWalkingMeters = Math.Min(request.MaxWalkingMeters, configMaxWalkingMeters);
        double walkingSpeed = _configuration.GetValue<double>("JourneyPlan:WalkingSpeedMetersPerSecond", 1.2);
        int maxCandidateStops = _configuration.GetValue<int>("JourneyPlan:MaxCandidateStops", 5);
        int transferBufferMinutes = _configuration.GetValue<int>("JourneyPlan:TransferBufferMinutes", 3);
        int maxTransferWalkMeters = _configuration.GetValue<int>("JourneyPlan:MaxTransferWalkMeters", 500);

        var utcTimeKey = request.DepartureDateTime!.Value.ToUniversalTime().ToString("yyyyMMdd_HHmm");
        
        string cacheKey = $"JourneyPlan:v2:{request.Origin.Lat}_{request.Origin.Lon}_{request.Destination.Lat}_{request.Destination.Lon}_{utcTimeKey}_{request.MaxTransfers}_{finalMaxWalkingMeters}_{request.MaxResults}_{request.IncludeIntermediateStops}_{walkingSpeed}_{maxCandidateStops}_{transferBufferMinutes}_{maxTransferWalkMeters}_{activeRun.Id}";

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
            FeedValidFrom = minDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            FeedValidTo = maxDate?.ToString("yyyy-MM-dd") ?? "UNKNOWN",
            IsFeedStale = maxDate.HasValue && maxDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
        };

        // 1. Get Active Stops from Cache or DB
        var activeStopsCache = await _cacheService.GetActiveStopsAsync(activeRun.Id, cancellationToken);
        var activeStops = activeStopsCache.Stops;
        if (!activeStops.Any()) 
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
            return response;
        }


        // 3. Find Origin and Destination Stops within walking distance
        var originStops = _spatialService.FindStopsWithinRadius(activeStops, request.Origin.Lat, request.Origin.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);
        var destStops = _spatialService.FindStopsWithinRadius(activeStops, request.Destination.Lat, request.Destination.Lon, finalMaxWalkingMeters, walkingSpeed, maxCandidateStops);

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

        var activeServiceIds = await _cacheService.GetActiveServiceIdsAsync(activeRun.Id, targetDate, cancellationToken);
        var previousDayServiceIds = new List<string>();

        // If requested time is early morning, consider previous day's trips that go past midnight (24:00+)
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

        // 5. Find Direct Routes (0-Transfer)
        int minWalkingTime = originStops.Min(x => x.WalkingTimeSeconds);
        int maxWaitTimeMinutes = _configuration.GetValue<int>("JourneyPlan:MaxWaitTimeMinutes", 60);
        int maxJourneyTimeMinutes = _configuration.GetValue<int>("JourneyPlan:MaxJourneyTimeMinutes", 240);
        int maxDirectTrips = _configuration.GetValue<int>("JourneyPlan:MaxDirectTrips", 500);
        var directTrips = await _routingEngine.FindDirectTripsAsync(originStopIds, destStopIds, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, maxJourneyTimeMinutes, cancellationToken);
        
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
                ToStopName = "VarÄ±ÅŸ NoktasÄ±",
                DepartureTime = leg.ArrivalTime,
                ArrivalTime = leg.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                DistanceMeters = dStop.DistanceMeters,
                DurationMinutes = dStop.WalkingTimeSeconds / 60
            };

            var serviceDate = trip.IsPreviousDayTrip ? targetDate.AddDays(-1).ToString("yyyy-MM-dd") : targetDate.ToString("yyyy-MM-dd");
            itineraries.Add(_mapper.CreateItineraryDto(request, new List<LegDto> { walk1, leg, walk2 }, serviceDate));
        }

        // 6. If 1-Transfer is requested, implement 1-transfer logic
        if (request.MaxTransfers >= 1)
        {
            int maxLegTrips = _configuration.GetValue<int>("JourneyPlan:MaxLegTrips", 500);
            int maxTransferTrips = _configuration.GetValue<int>("JourneyPlan:MaxTransferTrips", 150);
            
            var transferTrips = await _routingEngine.FindOneTransferTripsAsync(originStops, destStops, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, transferBufferMinutes * 60, targetDate, tzi, activeStopsCache, maxTransferWalkMeters, walkingSpeed, maxLegTrips, maxTransferTrips, maxWaitTimeMinutes, maxJourneyTimeMinutes, cancellationToken);
            
            foreach (var tResult in transferTrips.Take(request.MaxResults))
            {
                var oStop = originStops.First(x => x.Stop.StopId == tResult.Leg1.FromStopId);
                var dStop = destStops.First(x => x.Stop.StopId == tResult.Leg2.ToStopId);
                
                _logger.LogWarning("DEBUG WALK: o={O}, t={T}, d={D}, max={M}, res={Res}", oStop.DistanceMeters, tResult.TransferWalkMeters, dStop.DistanceMeters, finalMaxWalkingMeters, tResult.Leg1.TripId);
                if (oStop.DistanceMeters + tResult.TransferWalkMeters + dStop.DistanceMeters > finalMaxWalkingMeters) continue;
                
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
                    ToStopName = "VarÄ±ÅŸ NoktasÄ±",
                    DepartureTime = leg2.ArrivalTime,
                    ArrivalTime = leg2.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                    DistanceMeters = dStop.DistanceMeters,
                    DurationMinutes = dStop.WalkingTimeSeconds / 60
                };

                var serviceDate = tResult.Leg1.IsPreviousDayTrip ? targetDate.AddDays(-1).ToString("yyyy-MM-dd") : targetDate.ToString("yyyy-MM-dd");
                itineraries.Add(_mapper.CreateItineraryDto(request, new List<LegDto> { walk1, leg1, walkTransfer, leg2, walk2 }, serviceDate));
            }
        }

        // 7. If 2-Transfers is requested, implement 2-transfer logic
        if (request.MaxTransfers >= 2)
        {
            int maxLegTrips = _configuration.GetValue<int>("JourneyPlan:MaxLegTrips", 500);
            int maxTwoTransferTrips = _configuration.GetValue<int>("JourneyPlan:MaxTwoTransferTrips", 50);
            
            var twoTransferTrips = await _routingEngine.FindTwoTransferTripsAsync(originStops, destStops, activeServiceIds, previousDayServiceIds, requestedSeconds, minWalkingTime, transferBufferMinutes * 60, targetDate, tzi, activeStopsCache, maxTransferWalkMeters, walkingSpeed, maxLegTrips, maxTwoTransferTrips, maxWaitTimeMinutes, maxJourneyTimeMinutes, cancellationToken);
            
            foreach (var tResult in twoTransferTrips.Take(request.MaxResults))
            {
                var oStop = originStops.First(x => x.Stop.StopId == tResult.Leg1.FromStopId);
                var dStop = destStops.First(x => x.Stop.StopId == tResult.Leg3.ToStopId);
                
                if (oStop.DistanceMeters + tResult.TransferWalk1Meters + tResult.TransferWalk2Meters + dStop.DistanceMeters > finalMaxWalkingMeters) continue;
                
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
                    Mode = "WALK", FromStopId = dStop.Stop.StopId, FromStopName = dStop.Stop.StopName, ToStopId = "DEST", ToStopName = "VarÄ±ÅŸ NoktasÄ±",
                    DepartureTime = leg3.ArrivalTime, ArrivalTime = leg3.ArrivalTime.Value.AddSeconds(dStop.WalkingTimeSeconds),
                    DistanceMeters = dStop.DistanceMeters, DurationMinutes = dStop.WalkingTimeSeconds / 60
                };

                var serviceDate = tResult.Leg1.IsPreviousDayTrip ? targetDate.AddDays(-1).ToString("yyyy-MM-dd") : targetDate.ToString("yyyy-MM-dd");
                itineraries.Add(_mapper.CreateItineraryDto(request, new List<LegDto> { walk1, leg1, walkTransfer1, leg2, walkTransfer2, leg3, walk2 }, serviceDate));
            }
        }

        var topCandidates = itineraries
            .Where(x => x.TotalWalkingDistanceMeters <= finalMaxWalkingMeters)
            .OrderBy(x => x.ArrivalTime)
            .ThenBy(x => x.TransferCount)
            .ThenBy(x => x.TotalWalkingDistanceMeters)
            .ThenBy(x => x.TotalDurationMinutes)
            .ThenBy(x => x.TotalTransitStopCount)
            .ThenBy(x => string.Join("_", x.Legs.Select(l => l.TripId)))
            .Take(request.MaxResults + 5) // Buffer for dropped candidates
            .ToList();

        var evaluatedItineraries = await _mapper.EvaluateOsrmWalksAsync(topCandidates, request, activeStops, activeRun.Id, cancellationToken);

        response.Itineraries = evaluatedItineraries
            .Where(x => x.TotalWalkingDistanceMeters <= finalMaxWalkingMeters)
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
            await _mapper.PopulateIntermediateStopsAsync(response.Itineraries, tzi, activeRun.Id, cancellationToken);
        }

        if (response.Itineraries.Any())
        {
            response.ReasonCode = "SUCCESS";
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                Size = 1
            };
            cacheOptions.AddExpirationToken(_cacheTokenSource.GetChangeToken());
            _cache.Set(cacheKey, response, cacheOptions);
        }
        else
        {
            response.ReasonCode = "NO_ROUTE_FOUND";
        }

        return response;
    }
}
