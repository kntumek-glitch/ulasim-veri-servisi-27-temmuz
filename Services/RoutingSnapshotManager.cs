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

    public async Task<RoutingSnapshot> BuildCandidateSnapshotAsync(int importRunId, string feedHash, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
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
            var stopsSeq = trip.StopTimes.Select(st => st.StopId).ToList();
            string stopSequenceHash = string.Join(",", stopsSeq).GetHashCode().ToString("X");
            string patternId = $"P_{trip.ShapeId ?? "noshape"}_{trip.Route?.RouteId}_{trip.DirectionId}_{stopSequenceHash}";
            
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
                if (!snapshot.PatternToStops.ContainsKey(patternId))
                {
                    snapshot.PatternToStops[patternId] = stopsSeq;
                    
                    foreach (var stopId in stopsSeq)
                    {
                        if (!snapshot.StopToPatterns.ContainsKey(stopId))
                            snapshot.StopToPatterns[stopId] = new List<string>();
                        
                        if (!snapshot.StopToPatterns[stopId].Contains(patternId))
                            snapshot.StopToPatterns[stopId].Add(patternId);
                    }
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
            }
            snapshot.TripTimetables[trip.TripId] = timetables;
        }
        // Build bullet-proof O(log N) lookup indices for each stop on each pattern
        foreach (var patternId in snapshot.PatternToTrips.Keys)
        {
            var patternTrips = snapshot.PatternToTrips[patternId];
            var patternStops = snapshot.PatternToStops[patternId];
            
            for (int s = 0; s < patternStops.Count; s++)
            {
                string pKey = $"{patternStops[s]}_{patternId}";
                int[] depIndices = new int[patternTrips.Count];
                int[] arrIndices = new int[patternTrips.Count];
                
                for (int i = 0; i < patternTrips.Count; i++)
                {
                    depIndices[i] = i;
                    arrIndices[i] = i;
                }
                
                Array.Sort(depIndices, (i1, i2) => 
                {
                    var st1 = snapshot.TripTimetables[patternTrips[i1]];
                    var st2 = snapshot.TripTimetables[patternTrips[i2]];
                    int dep1 = st1.Count > s ? st1[s].DepartureSeconds : 0;
                    int dep2 = st2.Count > s ? st2[s].DepartureSeconds : 0;
                    int comp = dep1.CompareTo(dep2);
                    if (comp == 0) return i1.CompareTo(i2);
                    return comp;
                });
                
                Array.Sort(arrIndices, (i1, i2) => 
                {
                    var st1 = snapshot.TripTimetables[patternTrips[i1]];
                    var st2 = snapshot.TripTimetables[patternTrips[i2]];
                    int arr1 = st1.Count > s ? st1[s].ArrivalSeconds : 0;
                    int arr2 = st2.Count > s ? st2[s].ArrivalSeconds : 0;
                    int comp = arr1.CompareTo(arr2);
                    if (comp == 0) return i1.CompareTo(i2);
                    return comp;
                });
                
                snapshot.PatternStopDepartureIndices[pKey] = depIndices;
                snapshot.PatternStopArrivalIndices[pKey] = arrIndices;
            }
        }

        sw.Stop();
        snapshot.BuildDurationMs = sw.ElapsedMilliseconds;
        
        // Very rough memory estimation based on counts (assuming ~100 bytes per entry on average)
        snapshot.EstimatedMemoryBytes = (snapshot.Stops.Count * 120L) + 
                                        (snapshot.StopTransfers.Count * 150L) + 
                                        (snapshot.PatternMetadata.Count * 200L) + 
                                        (snapshot.TripTimetables.Count * 800L); // arrays of stoptimes

        _logger.LogInformation("Routing candidate snapshot built successfully. Patterns: {PatternCount}, Trips: {TripCount}, Duration: {BuildMs}ms", snapshot.PatternMetadata.Count, snapshot.TripTimetables.Count, snapshot.BuildDurationMs);
        return snapshot;
    }

    public void PromoteSnapshot(RoutingSnapshot candidate)
    {
        Interlocked.Exchange(ref _activeSnapshot, candidate);
        _logger.LogInformation("Routing snapshot swapped successfully. Feed Hash: {FeedHash}, Patterns: {PatternCount}", candidate.FeedHash, candidate.PatternMetadata.Count);
    }
}
