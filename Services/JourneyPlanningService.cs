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

    public JourneyPlanningService(AppDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
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

        // 2. Find Origin and Destination Stops within walking distance
        var originStops = FindStopsWithinRadius(activeStops, request.Origin.Lat, request.Origin.Lon, request.MaxWalkingMeters);
        var destStops = FindStopsWithinRadius(activeStops, request.Destination.Lat, request.Destination.Lon, request.MaxWalkingMeters);

        if (!originStops.Any() || !destStops.Any())
            return response;

        var originStopIds = originStops.Select(s => s.Stop.StopId).ToList();
        var destStopIds = destStops.Select(s => s.Stop.StopId).ToList();

        // 3. Find active ServiceIds for the given date
        var targetDate = request.DepartureDateTime.Date;
        var activeServiceIds = await GetActiveServiceIdsAsync(targetDate, cancellationToken);
        if (!activeServiceIds.Any()) return response;

        // Convert requested time to GTFS seconds (from midnight)
        var requestedSeconds = (int)request.DepartureDateTime.TimeOfDay.TotalSeconds;

        var itineraries = new List<ItineraryDto>();

        // 4. Find Direct Routes (0-Transfer)
        var directTrips = await FindDirectTripsAsync(originStopIds, destStopIds, activeServiceIds, requestedSeconds, cancellationToken);
        
        foreach (var trip in directTrips.Take(request.MaxResults))
        {
            var oStop = originStops.First(x => x.Stop.StopId == trip.OriginStopId);
            var dStop = destStops.First(x => x.Stop.StopId == trip.DestStopId);
            
            var leg = new LegDto
            {
                Mode = "TRANSIT",
                RouteId = trip.RouteId,
                RouteShortName = trip.RouteShortName,
                TripId = trip.TripId,
                FromStopId = trip.OriginStopId,
                FromStopName = oStop.Stop.StopName,
                DepartureTime = targetDate.AddSeconds(trip.DepartureSeconds),
                ToStopId = trip.DestStopId,
                ToStopName = dStop.Stop.StopName,
                ArrivalTime = targetDate.AddSeconds(trip.ArrivalSeconds),
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

        // 5. If 1-Transfer is requested, we can implement it here. (Placeholder for this iteration to keep it fast and responsive)
        if (request.MaxTransfers >= 1 && itineraries.Count < request.MaxResults)
        {
            // Placeholder: 1-transfer routing requires a more complex Graph search or SQL join which may time out.
            // For now, we prioritize direct routes.
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

    private async Task<List<DirectTripResult>> FindDirectTripsAsync(List<string> originStopIds, List<string> destStopIds, List<string> activeServiceIds, int requestedSeconds, CancellationToken cancellationToken)
    {
        // This is a heavy query if not optimized, but using EF Core nicely:
        var originStopTimes = _context.GtfsStopTimes.Where(st => originStopIds.Contains(st.StopId) && st.DepartureSeconds >= requestedSeconds);
        var destStopTimes = _context.GtfsStopTimes.Where(st => destStopIds.Contains(st.StopId));
        
        var query = from o in originStopTimes
                    join d in destStopTimes on o.GtfsTripId equals d.GtfsTripId
                    join t in _context.GtfsTrips on o.GtfsTripId equals t.Id
                    join r in _context.GtfsRoutes on t.GtfsRouteId equals r.Id
                    where d.StopSequence > o.StopSequence && activeServiceIds.Contains(t.ServiceId)
                    orderby o.DepartureSeconds
                    select new DirectTripResult
                    {
                        TripId = t.TripId,
                        RouteId = r.RouteId,
                        RouteShortName = r.RouteShortName,
                        OriginStopId = o.StopId,
                        DestStopId = d.StopId,
                        DepartureSeconds = o.DepartureSeconds.GetValueOrDefault(),
                        ArrivalSeconds = d.ArrivalSeconds.GetValueOrDefault()
                    };

        return await query.Take(50).AsNoTracking().ToListAsync(cancellationToken);
    }

    private List<StopWithDistance> FindStopsWithinRadius(List<GtfsStop> allStops, double lat, double lon, int maxMeters)
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
                    WalkingTimeSeconds = (int)(distance / 1.4) // Assume 1.4 m/s walking speed
                });
            }
        }
        return result.OrderBy(x => x.DistanceMeters).ToList();
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
    }
}
