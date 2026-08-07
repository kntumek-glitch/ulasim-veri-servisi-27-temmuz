using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransportDataService;
using ulasim_veri_servisi.Models.Routing;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services;

public class RoutingSnapshotManager : IRoutingSnapshotManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RoutingSnapshotManager> _logger;
    private RoutingSnapshot? _activeSnapshot;

    public RoutingSnapshotManager(IServiceScopeFactory scopeFactory, ILogger<RoutingSnapshotManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public RoutingSnapshot? GetActiveSnapshot() => _activeSnapshot;

    public async Task BuildAndSwapSnapshotAsync(int importRunId, string feedHash, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Building in-memory routing snapshot for Run ID {RunId}", importRunId);
        var snapshot = new RoutingSnapshot
        {
            ActiveImportId = importRunId,
            FeedHash = feedHash,
            CreatedAt = DateTime.UtcNow
        };

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // We use IgnoreQueryFilters to ensure we can build the snapshot even if the run is still in "Staging" (IsActive = false)
        var stops = await context.GtfsStops.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRunId).AsNoTracking().ToListAsync(cancellationToken);
        
        snapshot.StopsByIndex = new SnapshotStop[stops.Count];
        int stopIdx = 0;
        
        foreach (var stop in stops)
        {
            var snapStop = new SnapshotStop
            {
                StopId = stop.StopId,
                StopName = stop.StopName ?? "",
                StopLat = stop.StopLat,
                StopLon = stop.StopLon
            };
            snapshot.Stops[stop.StopId] = snapStop;
            
            snapshot.StopIdToIndex[stop.StopId] = stopIdx;
            snapshot.StopsByIndex[stopIdx] = snapStop;
            stopIdx++;
        }

        var transfers = await context.GtfsTransfers.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRunId).AsNoTracking().ToListAsync(cancellationToken);
        foreach (var tr in transfers)
        {
            if (!snapshot.StopTransfers.ContainsKey(tr.FromStopId))
                snapshot.StopTransfers[tr.FromStopId] = new List<SnapshotTransfer>();

            if (!snapshot.StopTransfersReverse.ContainsKey(tr.ToStopId))
                snapshot.StopTransfersReverse[tr.ToStopId] = new List<SnapshotTransfer>();

            var transferObj = new SnapshotTransfer
            {
                FromStopId = tr.FromStopId,
                ToStopId = tr.ToStopId,
                DistanceMeters = (int)tr.DistanceMeters,
                WalkingTimeSeconds = (int)tr.WalkingTimeSeconds
            };

            snapshot.StopTransfers[tr.FromStopId].Add(transferObj);
            snapshot.StopTransfersReverse[tr.ToStopId].Add(transferObj);
        }

        var calendars = await context.GtfsCalendars.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRunId).AsNoTracking().ToListAsync(cancellationToken);
        var calendarDates = await context.GtfsCalendarDates.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRunId).AsNoTracking().ToListAsync(cancellationToken);

        foreach (var cal in calendars)
        {
            var sc = new SnapshotCalendar
            {
                ServiceId = cal.ServiceId,
                Monday = cal.Monday,
                Tuesday = cal.Tuesday,
                Wednesday = cal.Wednesday,
                Thursday = cal.Thursday,
                Friday = cal.Friday,
                Saturday = cal.Saturday,
                Sunday = cal.Sunday,
                StartDate = cal.StartDate.ToString("yyyyMMdd"),
                EndDate = cal.EndDate.ToString("yyyyMMdd")
            };
            snapshot.ServiceCalendars[sc.ServiceId] = sc;
        }

        foreach (var cd in calendarDates)
        {
            if (!snapshot.ServiceCalendars.ContainsKey(cd.ServiceId))
            {
                snapshot.ServiceCalendars[cd.ServiceId] = new SnapshotCalendar { ServiceId = cd.ServiceId };
            }
            var sc = snapshot.ServiceCalendars[cd.ServiceId];
            if (cd.ExceptionType == 1) sc.AddedDates.Add(cd.Date.ToString("yyyyMMdd"));
            else if (cd.ExceptionType == 2) sc.RemovedDates.Add(cd.Date.ToString("yyyyMMdd"));
        }

        // Build Patterns and TripTimetables
        var trips = await context.GtfsTrips.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRunId)
            .Include(t => t.Route)
            .Include(t => t.StopTimes.OrderBy(st => st.StopSequence))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var trip in trips)
        {
            // Create pattern ID: ShapeId + RouteId + DirectionId
            string patternId = $"P_{trip.ShapeId ?? "noshape"}_{trip.Route?.RouteId}_{trip.DirectionId}";
            
            if (!snapshot.PatternMetadata.ContainsKey(patternId))
            {
                snapshot.PatternMetadata[patternId] = new PatternMetadata
                {
                    PatternId = patternId,
                    RouteId = trip.Route?.RouteId ?? "",
                    RouteShortName = trip.Route?.RouteShortName ?? "",
                    RouteType = trip.Route?.RouteType,
                    ShapeId = trip.ShapeId,
                    DirectionId = trip.DirectionId,
                    Headsign = trip.TripHeadsign
                };
                
                snapshot.PatternToTrips[patternId] = new List<string>();
                snapshot.PatternToStops[patternId] = trip.StopTimes.Select(st => st.StopId).ToList();
                
                // Map each stop in this pattern back to the pattern
                foreach (var stopId in snapshot.PatternToStops[patternId])
                {
                    if (!snapshot.StopToPatterns.ContainsKey(stopId))
                        snapshot.StopToPatterns[stopId] = new List<string>();
                    
                    if (!snapshot.StopToPatterns[stopId].Contains(patternId))
                        snapshot.StopToPatterns[stopId].Add(patternId);
                }
            }

            snapshot.PatternToTrips[patternId].Add(trip.TripId);
            snapshot.TripToServiceId[trip.TripId] = trip.ServiceId;
            
            var timetables = new List<SnapshotStopTime>();
            foreach (var st in trip.StopTimes)
            {
                timetables.Add(new SnapshotStopTime
                {
                    StopId = st.StopId,
                    StopSequence = st.StopSequence,
                    ArrivalSeconds = st.ArrivalSeconds ?? 0,
                    DepartureSeconds = st.DepartureSeconds ?? 0,
                    ArrivalTimeRaw = st.ArrivalTimeRaw,
                    DepartureTimeRaw = st.DepartureTimeRaw
                });
                
                var pKey = $"{st.StopId}_{patternId}";
                if (!snapshot.PatternStopDepartures.ContainsKey(pKey))
                    snapshot.PatternStopDepartures[pKey] = new List<int>();
                    
                snapshot.PatternStopDepartures[pKey].Add(st.DepartureSeconds ?? 0);
            }
            snapshot.TripTimetables[trip.TripId] = timetables;
        }

        // Sort PatternStopDepartures for fast O(log N) Binary Search lookups
        foreach (var kvp in snapshot.PatternStopDepartures)
        {
            kvp.Value.Sort();
        }

        // Atomic Swap
        Interlocked.Exchange(ref _activeSnapshot, snapshot);
        _logger.LogInformation("Routing snapshot swapped successfully. Feed Hash: {FeedHash}, Patterns: {PatternCount}, Trips: {TripCount}", feedHash, snapshot.PatternMetadata.Count, snapshot.TripTimetables.Count);
    }
}
