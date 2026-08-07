using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TransportDataService.Models.Gtfs.JourneyPlan;
using TransportDataService.Models.Exceptions;
using ulasim_veri_servisi.Models.Routing;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services;

public class RaptorRoutingEngine : IRaptorRoutingEngine
{
    private readonly IRoutingSnapshotManager _snapshotManager;
    private readonly WalkingRoutingService _walkingRoutingService;
    private readonly ILogger<RaptorRoutingEngine> _logger;
    private readonly IConfiguration _configuration;

    public RaptorRoutingEngine(
        IRoutingSnapshotManager snapshotManager,
        WalkingRoutingService walkingRoutingService,
        ILogger<RaptorRoutingEngine> logger,
        IConfiguration configuration)
    {
        _snapshotManager = snapshotManager;
        _walkingRoutingService = walkingRoutingService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<JourneyPlanSearchResponse> SearchJourneyV2Async(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken)
    {
        var snapshot = _snapshotManager.GetActiveSnapshot();
        if (snapshot == null)
        {
            _logger.LogWarning("Routing snapshot is not available. Search cannot be performed.");
            throw new SnapshotUnavailableException("Routing graph is not loaded or is currently updating.");
        }

        if (request.SearchMode == RoutingMode.ARRIVE_BY)
        {
            return await SearchArriveByJourneyV2Async(request, cancellationToken);
        }
        else if (request.SearchMode != RoutingMode.DEPART_AT)
        {
            throw new NotImplementedException("Only DEPART_AT and ARRIVE_BY modes are supported by the RAPTOR engine.");
        }

        // 1. Array allocation (RouteLabel[])
        int numStops = snapshot.StopsByIndex.Length;
        var labels = new RouteLabel[numStops];
        for (int i = 0; i < numStops; i++)
        {
            labels[i] = new RouteLabel
            {
                StopId = snapshot.StopsByIndex[i].StopId,
                StopIndex = i,
                AbsoluteArrivalSeconds = int.MaxValue,
                Round = -1,
                TotalWalkDurationSeconds = int.MaxValue,
                TotalWaitDurationSeconds = int.MaxValue
            };
        }

        // 2. Find nearby origin stops (Walking)
        var originStops = FindNearbyStops(snapshot, request.Origin.Lat, request.Origin.Lon, request.MaxWalkingMeters);
        if (!originStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified origin walking radius.", true);
        
        var destinationStops = FindNearbyStops(snapshot, request.Destination.Lat, request.Destination.Lon, request.MaxWalkingMeters);
        if (!destinationStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified destination walking radius.", false);

        var destStopsSet = destinationStops.Select(s => s.StopId).ToHashSet();
        
        DateTime searchDateToday = request.DateTime!.Value.Date;
        DateTime searchDateYesterday = searchDateToday.AddDays(-1);

        var activeServicesToday = new HashSet<string>();
        var activeServicesYesterday = new HashSet<string>();

        foreach (var sc in snapshot.ServiceCalendars.Values)
        {
            if (IsServiceActiveOnDate(sc, searchDateToday))
                activeServicesToday.Add(sc.ServiceId);
            if (IsServiceActiveOnDate(sc, searchDateYesterday))
                activeServicesYesterday.Add(sc.ServiceId);
        }
        
        if (!activeServicesToday.Any() && !activeServicesYesterday.Any())
        {
            throw new NoActiveServiceException("There are no active public transit services operating on this date.");
        }
        
        int prepBuffer = _configuration.GetValue<int>("JourneyPlan:BoardingPrepBufferSeconds", 60);
        int transferBuffer = _configuration.GetValue<int>("JourneyPlan:TransferSafetyBufferSeconds", 120);
        
        int departureTimeSeconds = (int)request.DateTime!.Value.TimeOfDay.TotalSeconds;
        int globalBestArrivalTime = int.MaxValue;

        var activeStops = new HashSet<int>();

        // Initialize Origin Stops
        foreach (var os in originStops)
        {
            if (snapshot.StopIdToIndex.TryGetValue(os.StopId, out int idx))
            {
                labels[idx].AbsoluteArrivalSeconds = departureTimeSeconds + os.WalkingDurationSeconds + prepBuffer;
                labels[idx].TotalWalkDurationSeconds = os.WalkingDurationSeconds;
                labels[idx].TotalWaitDurationSeconds = 0;
                labels[idx].Round = -1;
                activeStops.Add(idx);
                
                if (destStopsSet.Contains(os.StopId))
                {
                    globalBestArrivalTime = Math.Min(globalBestArrivalTime, labels[idx].AbsoluteArrivalSeconds);
                }
            }
        }

        // RAPTOR Loop
        int maxRounds = Math.Min(3, request.MaxTransfers + 1); // 0 = Direct, 1 = 1-Transfer, 2 = 2-Transfer
        for (int k = 0; k < maxRounds; k++)
        {
            if (activeStops.Count == 0) break; // Pruning condition

            var newlyActiveStops = new HashSet<int>();
            var activePatterns = new HashSet<string>();

            // Route Scan: find active patterns
            foreach (var stopIdx in activeStops)
            {
                if (labels[stopIdx].AbsoluteArrivalSeconds >= globalBestArrivalTime)
                {
                    // UPPER BOUND PRUNING: Branch is mathematically proven to be sub-optimal
                    continue; 
                }

                string sId = snapshot.StopsByIndex[stopIdx].StopId;
                if (snapshot.StopToPatterns.TryGetValue(sId, out var patterns))
                {
                    foreach (var p in patterns)
                        activePatterns.Add(p);
                }
            }

            // Iterate active patterns
            foreach (var patternId in activePatterns)
            {
                // In a real RAPTOR, we board at the earliest active stop and propagate.
                // For simplicity, we just evaluate all trip departures from active stops on this pattern.
                var stopsOnPattern = snapshot.PatternToStops[patternId];
                
                int? currentTripIndex = null;
                int? boardedStopIdx = null;
                int currentTripOffset = 0;
                
                foreach (var stopId in stopsOnPattern)
                {
                    if (!snapshot.StopIdToIndex.TryGetValue(stopId, out int stopIdx)) continue;
                    
                    if (currentTripIndex.HasValue)
                    {
                        // We are on a trip, we can disembark here
                        string currentTripId = snapshot.PatternToTrips[patternId][currentTripIndex.Value];
                        var timetable = snapshot.TripTimetables[currentTripId];
                        var stopTime = timetable.First(x => x.StopId == stopId);
                        
                        int arrivalTime = stopTime.ArrivalSeconds + currentTripOffset;
                        int walkTime = labels[boardedStopIdx.Value].TotalWalkDurationSeconds;
                        int waitTime = labels[boardedStopIdx.Value].TotalWaitDurationSeconds; 
                        
                        // Wait time = Departure Time from boarded stop - Arrival time at boarded stop
                        var boardStopTime = timetable.First(x => x.StopId == snapshot.StopsByIndex[boardedStopIdx.Value].StopId);
                        int boardAbsoluteTime = boardStopTime.DepartureSeconds + currentTripOffset;
                        int additionalWait = boardAbsoluteTime - labels[boardedStopIdx.Value].AbsoluteArrivalSeconds;
                        waitTime += additionalWait;

                        var newLabel = new RouteLabel
                        {
                            StopId = stopId,
                            StopIndex = stopIdx,
                            AbsoluteArrivalSeconds = arrivalTime,
                            Round = k,
                            TotalWalkDurationSeconds = walkTime,
                            TotalWaitDurationSeconds = waitTime,
                            PreviousStopId = snapshot.StopsByIndex[boardedStopIdx.Value].StopId,
                            PreviousTripId = currentTripId,
                            PreviousPatternId = patternId,
                            BoardingStopId = snapshot.StopsByIndex[boardedStopIdx.Value].StopId,
                            UsedTransferEdge = false
                        };

                        if (Dominates(newLabel, labels[stopIdx]))
                        {
                            labels[stopIdx] = newLabel;
                            newlyActiveStops.Add(stopIdx);
                            
                            if (destStopsSet.Contains(stopId))
                            {
                                globalBestArrivalTime = Math.Min(globalBestArrivalTime, arrivalTime);
                            }
                        }
                    }

                    // Can we board here or find an earlier trip?
                    if (activeStops.Contains(stopIdx) && labels[stopIdx].AbsoluteArrivalSeconds < globalBestArrivalTime)
                    {
                        // EBT: Must wait for Boarding/Transfer buffer
                        int ebt = labels[stopIdx].AbsoluteArrivalSeconds;
                        // Transfer buffer is applied implicitly during Transfer Relaxation.
                        // However, prepBuffer is added at Origin.

                        int bestTripIndex = FindEarliestTripIndex(snapshot, patternId, stopId, ebt, activeServicesToday, activeServicesYesterday, out int offsetSeconds);
                        if (bestTripIndex != -1)
                        {
                            if (!currentTripIndex.HasValue || bestTripIndex < currentTripIndex.Value)
                            {
                                currentTripIndex = bestTripIndex;
                                boardedStopIdx = stopIdx;
                                currentTripOffset = offsetSeconds;
                            }
                        }
                    }
                }
            }

            // Transfer Relaxation
            var transferActiveStops = new HashSet<int>();
            foreach (var stopIdx in newlyActiveStops)
            {
                string sId = snapshot.StopsByIndex[stopIdx].StopId;
                if (snapshot.StopTransfers.TryGetValue(sId, out var transfers))
                {
                    foreach (var tr in transfers)
                    {
                        if (snapshot.StopIdToIndex.TryGetValue(tr.ToStopId, out int toIdx))
                        {
                            // Apply transfer safety buffer here
                            int arrival = labels[stopIdx].AbsoluteArrivalSeconds + tr.WalkingTimeSeconds + transferBuffer;
                            var newLabel = new RouteLabel
                            {
                                StopId = tr.ToStopId,
                                StopIndex = toIdx,
                                AbsoluteArrivalSeconds = arrival,
                                Round = k,
                                TotalWalkDurationSeconds = labels[stopIdx].TotalWalkDurationSeconds + tr.WalkingTimeSeconds,
                                TotalWaitDurationSeconds = labels[stopIdx].TotalWaitDurationSeconds,
                                PreviousStopId = sId,
                                UsedTransferEdge = true
                            };

                            if (Dominates(newLabel, labels[toIdx]))
                            {
                                labels[toIdx] = newLabel;
                                transferActiveStops.Add(toIdx);
                                
                                if (destStopsSet.Contains(tr.ToStopId))
                                {
                                    globalBestArrivalTime = Math.Min(globalBestArrivalTime, arrival);
                                }
                            }
                        }
                    }
                }
            }

            activeStops = newlyActiveStops.Concat(transferActiveStops).ToHashSet();
        }

        // Backtrack from Destination Stops
        var itineraries = new List<ItineraryDto>();
        foreach (var destStop in destinationStops)
        {
            if (!snapshot.StopIdToIndex.TryGetValue(destStop.StopId, out int destIdx)) continue;
            var finalLabel = labels[destIdx];
            if (finalLabel.AbsoluteArrivalSeconds == int.MaxValue) continue;
            
            // Reconstruct backward
            var legs = new List<LegDto>();
            int currIdx = destIdx;
            
            while (currIdx != -1)
            {
                var curr = labels[currIdx];
                if (curr.Round == -1) // Reached origin
                {
                    var originWalk = originStops.FirstOrDefault(x => x.StopId == curr.StopId);
                    if (originWalk != null)
                    {
                        var wr = await _walkingRoutingService.CalculateWalkingRouteAsync(
                            request.Origin.Lat, request.Origin.Lon, 
                            snapshot.StopsByIndex[currIdx].StopLat, snapshot.StopsByIndex[currIdx].StopLon, 
                            request.IncludeWalkingGeometry, cancellationToken);
                            
                        legs.Add(new LegDto
                        {
                            Mode = "WALK",
                            DurationSeconds = wr.State.IsSuccess ? (int)wr.DurationSeconds : originWalk.WalkingDurationSeconds,
                            DistanceMeters = wr.State.IsSuccess ? (int)wr.DistanceMeters : 0,
                            GeometryGeoJson = wr.GeometryGeoJson,
                            FromStopName = "Origin",
                            ToStopId = curr.StopId,
                            ToStopName = snapshot.StopsByIndex[currIdx].StopName,
                            WalkingSource = wr.State.IsSuccess ? "OSRM" : "Haversine",
                            IsApproximate = !wr.State.IsSuccess,
                            WalkingWarning = wr.State.ErrorMessage,
                            HasGeometry = wr.GeometryGeoJson != null
                        });
                    }
                    break;
                }
                
                if (curr.UsedTransferEdge)
                {
                    var prevIdx = snapshot.StopIdToIndex[curr.PreviousStopId];
                    var transfer = snapshot.StopTransfers[curr.PreviousStopId].First(x => x.ToStopId == curr.StopId);
                    legs.Add(new LegDto
                    {
                        Mode = "WALK",
                        DurationSeconds = transfer.WalkingTimeSeconds,
                        DistanceMeters = transfer.DistanceMeters,
                        FromStopId = curr.PreviousStopId,
                        FromStopName = snapshot.StopsByIndex[prevIdx].StopName,
                        ToStopId = curr.StopId,
                        ToStopName = snapshot.StopsByIndex[currIdx].StopName,
                        WalkingSource = "Haversine",
                        IsApproximate = true,
                        WalkingWarning = "Station-to-station static transfer",
                        HasGeometry = false
                    });
                    currIdx = prevIdx;
                }
                else
                {
                    var boardIdx = snapshot.StopIdToIndex[curr.BoardingStopId];
                    var boardTime = snapshot.TripTimetables[curr.PreviousTripId].First(x => x.StopId == curr.BoardingStopId);
                    var alightTime = snapshot.TripTimetables[curr.PreviousTripId].First(x => x.StopId == curr.StopId);
                    
                    int? rType = null;
                    string rId = "", rName = "";
                    if (snapshot.PatternMetadata.TryGetValue(curr.PreviousPatternId, out var meta))
                    {
                        rType = meta.RouteType;
                        rId = meta.RouteId;
                        rName = meta.RouteShortName;
                    }
                    
                    legs.Add(new LegDto
                    {
                        Mode = "TRANSIT",
                        TripId = curr.PreviousTripId,
                        PatternId = curr.PreviousPatternId,
                        RouteId = rId,
                        RouteShortName = rName,
                        RouteType = rType,
                        FromStopId = curr.BoardingStopId,
                        FromStopName = snapshot.StopsByIndex[boardIdx].StopName,
                        ToStopId = curr.StopId,
                        ToStopName = snapshot.StopsByIndex[currIdx].StopName,
                        RawGtfsDepartureSeconds = boardTime.DepartureSeconds,
                        RawGtfsArrivalSeconds = alightTime.ArrivalSeconds,
                        DurationSeconds = alightTime.ArrivalSeconds - boardTime.DepartureSeconds
                    });
                    currIdx = boardIdx;
                }
            }
            
            legs.Reverse();
            
            // Final Walk (Door-to-Door)
            var wrDest = await _walkingRoutingService.CalculateWalkingRouteAsync(
                snapshot.StopsByIndex[destIdx].StopLat, snapshot.StopsByIndex[destIdx].StopLon,
                request.Destination.Lat, request.Destination.Lon, request.IncludeWalkingGeometry, cancellationToken);
                
            int actualFinalWalkDuration = wrDest.State.IsSuccess ? (int)wrDest.DurationSeconds : destStop.WalkingDurationSeconds;
            
            legs.Add(new LegDto
            {
                Mode = "WALK",
                DurationSeconds = actualFinalWalkDuration,
                DistanceMeters = wrDest.State.IsSuccess ? (int)wrDest.DistanceMeters : 0,
                GeometryGeoJson = wrDest.GeometryGeoJson,
                FromStopId = destStop.StopId,
                FromStopName = snapshot.StopsByIndex[destIdx].StopName,
                ToStopName = "Destination",
                WalkingSource = wrDest.State.IsSuccess ? "OSRM" : "Haversine",
                IsApproximate = !wrDest.State.IsSuccess,
                WalkingWarning = wrDest.State.ErrorMessage,
                HasGeometry = wrDest.GeometryGeoJson != null
            });
            
            // Forward Cascade Simulation!
            bool isValid = CascadeSimulateItinerary(legs, searchDateToday, departureTimeSeconds, prepBuffer, transferBuffer, snapshot, activeServicesToday, activeServicesYesterday, out int finalArrivalTimeSeconds);
            if (!isValid) continue; // Itinerary broken by OSRM delay

            bool isApprox = legs.Any(l => l.Mode == "WALK" && l.IsApproximate);

            var iti = new ItineraryDto
            {
                Legs = legs,
                TotalWalkingDistanceMeters = legs.Where(l => l.Mode == "WALK").Sum(l => l.DistanceMeters),
                TotalWalkingTimeSeconds = legs.Where(l => l.Mode == "WALK").Sum(l => l.DurationSeconds),
                TransferCount = legs.Count(l => l.Mode == "TRANSIT") - 1, // Recalculate based on actual transit legs
                TotalWaitingTimeSeconds = 0, // Cascade simulator doesn't easily compute this, can be derived later if needed
                TotalInVehicleTimeSeconds = legs.Where(l => l.Mode == "TRANSIT").Sum(l => l.DurationSeconds),
                ArrivalTime = searchDateToday.AddSeconds(finalArrivalTimeSeconds),
                DepartureTime = searchDateToday.AddSeconds(departureTimeSeconds),
                IsApproximate = isApprox
            };
            
            iti.PlanId = GeneratePlanId(iti, snapshot.FeedHash, request.SearchMode.ToString());
            
            itineraries.Add(iti);
        }

        // Global Route Sorting Hierarchy
        itineraries = itineraries
            .OrderBy(x => x.ArrivalTime) // Priority 1: Earliest DoorToDoorArrivalTime
            .ThenBy(x => x.TransferCount) // Priority 2: Least Transfer Count
            .ThenBy(x => x.TotalWalkingTimeSeconds) // Priority 3: Least Walk
            .ThenBy(x => x.TotalWaitingTimeSeconds) // Priority 4: Least Wait
            .ThenBy(x => string.Join("_", x.Legs.Where(l => l.Mode == "TRANSIT").Select(l => l.RouteId))) // Priority 5: Deterministic Tie-breaker
            .Take(request.MaxResults)
            .ToList();

        return new JourneyPlanSearchResponse { Itineraries = itineraries };
    }

    private bool CascadeSimulateItinerary(List<LegDto> legs, DateTime searchDate, int currentTimeSeconds, int prepBuffer, int transferBuffer, RoutingSnapshot snapshot, HashSet<string> activeToday, HashSet<string> activeYesterday, out int finalArrivalTimeSeconds)
    {
        finalArrivalTimeSeconds = currentTimeSeconds;
        DateTime midnight = searchDate.Date;

        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            if (leg.Mode == "WALK")
            {
                if (i == 0) // Origin
                {
                    currentTimeSeconds += leg.DurationSeconds + prepBuffer;
                }
                else if (i == legs.Count - 1) // Destination
                {
                    currentTimeSeconds += leg.DurationSeconds;
                }
                else // Transfer
                {
                    currentTimeSeconds += leg.DurationSeconds + transferBuffer;
                }
            }
            else if (leg.Mode == "TRANSIT")
            {
                int bestTripIdx = FindEarliestTripIndex(snapshot, leg.PatternId!, leg.FromStopId!, currentTimeSeconds, activeToday, activeYesterday, out int offsetSeconds);
                if (bestTripIdx == -1) return false;

                string tripId = snapshot.PatternToTrips[leg.PatternId!][bestTripIdx];
                var timetable = snapshot.TripTimetables[tripId];
                var boardTime = timetable.First(x => x.StopId == leg.FromStopId);
                var alightTime = timetable.First(x => x.StopId == leg.ToStopId);

                leg.TripId = tripId;
                leg.RawGtfsDepartureSeconds = boardTime.DepartureSeconds;
                leg.RawGtfsArrivalSeconds = alightTime.ArrivalSeconds;
                leg.DurationSeconds = alightTime.ArrivalSeconds - boardTime.DepartureSeconds;
                
                int absDeparture = boardTime.DepartureSeconds + offsetSeconds;
                int absArrival = alightTime.ArrivalSeconds + offsetSeconds;

                leg.DepartureTime = midnight.AddSeconds(absDeparture);
                leg.ArrivalTime = midnight.AddSeconds(absArrival);

                currentTimeSeconds = absArrival;
            }
        }
        
        finalArrivalTimeSeconds = currentTimeSeconds;
        return true;
    }

    private bool Dominates(RouteLabel newLabel, RouteLabel oldLabel)
    {
        if (newLabel.AbsoluteArrivalSeconds < oldLabel.AbsoluteArrivalSeconds) return true;
        if (newLabel.AbsoluteArrivalSeconds > oldLabel.AbsoluteArrivalSeconds) return false;

        if (newLabel.TotalWalkDurationSeconds < oldLabel.TotalWalkDurationSeconds) return true;
        if (newLabel.TotalWalkDurationSeconds > oldLabel.TotalWalkDurationSeconds) return false;

        return newLabel.TotalWaitDurationSeconds < oldLabel.TotalWaitDurationSeconds;
    }

    private int FindEarliestTripIndex(RoutingSnapshot snapshot, string patternId, string stopId, int ebt, HashSet<string> activeToday, HashSet<string> activeYesterday, out int offsetSeconds)
    {
        var trips = snapshot.PatternToTrips[patternId];
        offsetSeconds = 0;
        
        int bestTodayIdx = -1;
        int bestTodayDep = int.MaxValue;
        
        for(int i = 0; i < trips.Count; i++)
        {
            string tId = trips[i];
            var st = snapshot.TripTimetables[tId].FirstOrDefault(x => x.StopId == stopId);
            if (st == null) continue;
            
            if (st.DepartureSeconds >= ebt)
            {
                string sId = snapshot.TripToServiceId[tId];
                if (activeToday.Contains(sId))
                {
                    bestTodayIdx = i;
                    bestTodayDep = st.DepartureSeconds;
                    break;
                }
            }
        }
        
        int bestYesterdayIdx = -1;
        int bestYesterdayDep = int.MaxValue;
        
        int yesterdayEbt = ebt + 86400;
        for(int i = 0; i < trips.Count; i++)
        {
            string tId = trips[i];
            var st = snapshot.TripTimetables[tId].FirstOrDefault(x => x.StopId == stopId);
            if (st == null) continue;
            
            if (st.DepartureSeconds >= yesterdayEbt)
            {
                string sId = snapshot.TripToServiceId[tId];
                if (activeYesterday.Contains(sId))
                {
                    bestYesterdayIdx = i;
                    bestYesterdayDep = st.DepartureSeconds - 86400;
                    break;
                }
            }
        }
        
        if (bestTodayIdx != -1 && bestYesterdayIdx != -1)
        {
            if (bestYesterdayDep < bestTodayDep)
            {
                offsetSeconds = -86400;
                return bestYesterdayIdx;
            }
            offsetSeconds = 0;
            return bestTodayIdx;
        }
        else if (bestYesterdayIdx != -1)
        {
            offsetSeconds = -86400;
            return bestYesterdayIdx;
        }
        else if (bestTodayIdx != -1)
        {
            offsetSeconds = 0;
            return bestTodayIdx;
        }
        
        return -1;
    }

    private class LocalWalkEdge
    {
        public string StopId { get; set; } = string.Empty;
        public int WalkingDurationSeconds { get; set; }
    }

    private List<LocalWalkEdge> FindNearbyStops(RoutingSnapshot snapshot, double lat, double lon, int maxMeters)
    {
        var result = new List<LocalWalkEdge>();
        for (int i = 0; i < snapshot.StopsByIndex.Length; i++)
        {
            var stop = snapshot.StopsByIndex[i];
            double dist = GetHaversineDistance(lat, lon, stop.StopLat, stop.StopLon);
            if (dist <= maxMeters)
            {
                result.Add(new LocalWalkEdge
                {
                    StopId = stop.StopId,
                    WalkingDurationSeconds = (int)(dist / 1.4) // 1.4 m/s walking speed
                });
            }
        }
        return result;
    }

    private const double EarthRadiusMeters = 6371000;
    
    private double GetHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        
        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    private bool IsServiceActiveOnDate(SnapshotCalendar cal, DateTime date)
    {
        string dateStr = date.ToString("yyyyMMdd");
        if (cal.RemovedDates.Contains(dateStr)) return false;
        if (cal.AddedDates.Contains(dateStr)) return true;

        if (string.IsNullOrEmpty(cal.StartDate) || string.IsNullOrEmpty(cal.EndDate)) return false;

        if (string.Compare(dateStr, cal.StartDate) < 0 || string.Compare(dateStr, cal.EndDate) > 0) return false;

        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => cal.Monday,
            DayOfWeek.Tuesday => cal.Tuesday,
            DayOfWeek.Wednesday => cal.Wednesday,
            DayOfWeek.Thursday => cal.Thursday,
            DayOfWeek.Friday => cal.Friday,
            DayOfWeek.Saturday => cal.Saturday,
            DayOfWeek.Sunday => cal.Sunday,
            _ => false
        };
    }

    private string GeneratePlanId(ItineraryDto itinerary, string feedHash, string searchMode)
    {
        var sb = new StringBuilder();
        sb.Append(feedHash).Append('|');
        sb.Append(itinerary.DepartureTime.Date.ToString("yyyyMMdd")).Append('|');
        sb.Append(searchMode).Append('|');

        var transitLegs = itinerary.Legs.Where(l => l.Mode == "TRANSIT").ToList();
        sb.Append("Trips:");
        foreach (var leg in transitLegs) sb.Append(leg.TripId).Append(',');
        sb.Append("|Boards:");
        foreach (var leg in transitLegs) sb.Append(leg.FromStopId).Append(',');
        sb.Append("|Alights:");
        foreach (var leg in transitLegs) sb.Append(leg.ToStopId).Append(',');
        
        var transferLegs = itinerary.Legs.Where(l => l.Mode == "WALK" && !string.IsNullOrEmpty(l.FromStopId) && !string.IsNullOrEmpty(l.ToStopId) && l.FromStopId != l.ToStopId).ToList();
        sb.Append("|Transfers:");
        foreach (var leg in transferLegs) sb.Append(leg.FromStopId).Append('>').Append(leg.ToStopId).Append(',');

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = sha256.ComputeHash(bytes);
        return "JP-" + BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
    }
        private bool DominatesBackward(BackwardRouteLabel newLabel, BackwardRouteLabel oldLabel)
    {
        if (newLabel.AbsoluteDepartureSeconds > oldLabel.AbsoluteDepartureSeconds) return true;
        if (newLabel.AbsoluteDepartureSeconds < oldLabel.AbsoluteDepartureSeconds) return false;

        if (newLabel.TotalWalkDurationSeconds < oldLabel.TotalWalkDurationSeconds) return true;
        if (newLabel.TotalWalkDurationSeconds > oldLabel.TotalWalkDurationSeconds) return false;

        return newLabel.TotalWaitDurationSeconds < oldLabel.TotalWaitDurationSeconds;
    }

    private int FindLatestTripIndex(RoutingSnapshot snapshot, string patternId, string stopId, int latestArrival, HashSet<string> activeToday, HashSet<string> activeYesterday, out int offsetSeconds)
    {
        var trips = snapshot.PatternToTrips[patternId];
        offsetSeconds = 0;
        
        int bestTodayIdx = -1;
        int bestTodayArr = -1;
        
        for(int i = 0; i < trips.Count; i++)
        {
            string tId = trips[i];
            var st = snapshot.TripTimetables[tId].FirstOrDefault(x => x.StopId == stopId);
            if (st == null) continue;
            
            if (st.ArrivalSeconds <= latestArrival)
            {
                string sId = snapshot.TripToServiceId[tId];
                if (activeToday.Contains(sId))
                {
                    // Update if it's a later trip
                    if (st.ArrivalSeconds > bestTodayArr || (st.ArrivalSeconds == bestTodayArr && i > bestTodayIdx))
                    {
                        bestTodayIdx = i;
                        bestTodayArr = st.ArrivalSeconds;
                    }
                }
            }
        }
        
        int bestYesterdayIdx = -1;
        int bestYesterdayArr = -1;
        
        int yesterdayArrival = latestArrival + 86400;
        for(int i = 0; i < trips.Count; i++)
        {
            string tId = trips[i];
            var st = snapshot.TripTimetables[tId].FirstOrDefault(x => x.StopId == stopId);
            if (st == null) continue;
            
            if (st.ArrivalSeconds <= yesterdayArrival)
            {
                string sId = snapshot.TripToServiceId[tId];
                if (activeYesterday.Contains(sId))
                {
                    if (st.ArrivalSeconds > bestYesterdayArr || (st.ArrivalSeconds == bestYesterdayArr && i > bestYesterdayIdx))
                    {
                        bestYesterdayIdx = i;
                        bestYesterdayArr = st.ArrivalSeconds;
                    }
                }
            }
        }
        
        if (bestTodayIdx != -1 && bestYesterdayIdx != -1)
        {
            if (bestTodayArr > (bestYesterdayArr - 86400))
            {
                offsetSeconds = 0;
                return bestTodayIdx;
            }
            offsetSeconds = -86400;
            return bestYesterdayIdx;
        }
        else if (bestTodayIdx != -1)
        {
            offsetSeconds = 0;
            return bestTodayIdx;
        }
        else if (bestYesterdayIdx != -1)
        {
            offsetSeconds = -86400;
            return bestYesterdayIdx;
        }
        
        return -1;
    }
    private ItineraryDto? ReconstructBackwardItinerary(
        RoutingSnapshot snapshot, 
        BackwardRouteLabel[] labels, 
        int originStopIdx, 
        LocalWalkEdge originWalk, 
        LocalWalkEdge destWalk, 
        JourneyPlanV2SearchRequest request, 
        int targetArrivalTimeSeconds, 
        int prepBuffer)
    {
        var legs = new List<LegDto>();
        int currIdx = originStopIdx;
        
        // Origin walk is added later by CascadeSimulator or we can add it here as Haversine fallback.
        // Actually Cascade simulator expects all legs to be present, and it will re-evaluate them.
        
        legs.Add(new LegDto
        {
            Mode = "WALK",
            DurationSeconds = originWalk.WalkingDurationSeconds,
            DistanceMeters = (int)(originWalk.WalkingDurationSeconds * 1.4),
            FromStopName = "Origin",
            ToStopId = snapshot.StopsByIndex[currIdx].StopId,
            ToStopName = snapshot.StopsByIndex[currIdx].StopName,
            WalkingSource = "Haversine",
            IsApproximate = true,
            HasGeometry = false
        });
        
        while (currIdx != -1)
        {
            var curr = labels[currIdx];
            if (curr.Round == -1) // Reached destination
            {
                if (destWalk != null)
                {
                    legs.Add(new LegDto
                    {
                        Mode = "WALK",
                        DurationSeconds = destWalk.WalkingDurationSeconds,
                        DistanceMeters = (int)(destWalk.WalkingDurationSeconds * 1.4),
                        FromStopId = curr.StopId,
                        FromStopName = snapshot.StopsByIndex[currIdx].StopName,
                        ToStopName = "Destination",
                        WalkingSource = "Haversine",
                        IsApproximate = true,
                        HasGeometry = false
                    });
                }
                break;
            }
            
            if (curr.UsedTransferEdge)
            {
                var nextIdx = snapshot.StopIdToIndex[curr.NextStopId];
                var transfer = snapshot.StopTransfers[curr.StopId].First(x => x.ToStopId == curr.NextStopId);
                legs.Add(new LegDto
                {
                    Mode = "WALK",
                    DurationSeconds = transfer.WalkingTimeSeconds,
                    DistanceMeters = transfer.DistanceMeters,
                    FromStopId = curr.StopId,
                    FromStopName = snapshot.StopsByIndex[currIdx].StopName,
                    ToStopId = curr.NextStopId,
                    ToStopName = snapshot.StopsByIndex[nextIdx].StopName,
                    WalkingSource = "Haversine",
                    IsApproximate = true,
                    WalkingWarning = "Station-to-station static transfer",
                    HasGeometry = false
                });
                currIdx = nextIdx;
            }
            else
            {
                var alightIdx = snapshot.StopIdToIndex[curr.AlightingStopId];
                var boardTime = snapshot.TripTimetables[curr.NextTripId].First(x => x.StopId == curr.StopId);
                var alightTime = snapshot.TripTimetables[curr.NextTripId].First(x => x.StopId == curr.AlightingStopId);
                
                int? rType = null;
                string rId = "", rName = "";
                if (snapshot.PatternMetadata.TryGetValue(curr.NextPatternId, out var meta))
                {
                    rType = meta.RouteType;
                    rId = meta.RouteId;
                    rName = meta.RouteShortName;
                }
                
                legs.Add(new LegDto
                {
                    Mode = "TRANSIT",
                    TripId = curr.NextTripId,
                    PatternId = curr.NextPatternId,
                    RouteId = rId,
                    RouteShortName = rName,
                    RouteType = rType,
                    FromStopId = curr.StopId,
                    FromStopName = snapshot.StopsByIndex[currIdx].StopName,
                    ToStopId = curr.AlightingStopId,
                    ToStopName = snapshot.StopsByIndex[alightIdx].StopName,
                    RawGtfsDepartureSeconds = boardTime.DepartureSeconds,
                    RawGtfsArrivalSeconds = alightTime.ArrivalSeconds,
                    DurationSeconds = alightTime.ArrivalSeconds - boardTime.DepartureSeconds
                });
                currIdx = alightIdx;
            }
        }
        
        var iti = new ItineraryDto
        {
            Legs = legs,
            TotalWalkingDistanceMeters = legs.Where(l => l.Mode == "WALK").Sum(l => l.DistanceMeters),
            TotalWalkingTimeSeconds = legs.Where(l => l.Mode == "WALK").Sum(l => l.DurationSeconds),
            TransferCount = legs.Count(l => l.Mode == "TRANSIT") - 1,
            TotalWaitingTimeSeconds = 0,
            TotalInVehicleTimeSeconds = legs.Where(l => l.Mode == "TRANSIT").Sum(l => l.DurationSeconds),
            // The cascade simulator will overwrite ArrivalTime and DepartureTime
            DepartureTime = request.DateTime!.Value,
            ArrivalTime = request.DateTime!.Value
        };
        
        return iti;
    }
    public async Task<JourneyPlanSearchResponse> SearchArriveByJourneyV2Async(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken)
    {
        var snapshot = _snapshotManager.GetActiveSnapshot();
        if (snapshot == null)
            throw new SnapshotUnavailableException("Routing graph is not loaded or is currently updating.");

        int numStops = snapshot.StopsByIndex.Length;
        var labels = new BackwardRouteLabel[numStops];
        for (int i = 0; i < numStops; i++)
        {
            labels[i] = new BackwardRouteLabel
            {
                StopId = snapshot.StopsByIndex[i].StopId,
                StopIndex = i,
                AbsoluteDepartureSeconds = -1, // -1 means unreachable
                Round = -1,
                TotalWalkDurationSeconds = int.MaxValue,
                TotalWaitDurationSeconds = int.MaxValue
            };
        }

        var originStops = FindNearbyStops(snapshot, request.Origin.Lat, request.Origin.Lon, request.MaxWalkingMeters);
        if (!originStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified origin walking radius.", true);
        
        var destinationStops = FindNearbyStops(snapshot, request.Destination.Lat, request.Destination.Lon, request.MaxWalkingMeters);
        if (!destinationStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified destination walking radius.", false);

        var origStopsSet = originStops.Select(s => s.StopId).ToHashSet();
        
        DateTime searchDateToday = request.DateTime!.Value.Date;
        DateTime searchDateYesterday = searchDateToday.AddDays(-1);

        var activeServicesToday = new HashSet<string>();
        var activeServicesYesterday = new HashSet<string>();

        foreach (var sc in snapshot.ServiceCalendars.Values)
        {
            if (IsServiceActiveOnDate(sc, searchDateToday))
                activeServicesToday.Add(sc.ServiceId);
            if (IsServiceActiveOnDate(sc, searchDateYesterday))
                activeServicesYesterday.Add(sc.ServiceId);
        }
        
        if (!activeServicesToday.Any() && !activeServicesYesterday.Any())
            throw new NoActiveServiceException("There are no active public transit services operating on this date.");

        int prepBuffer = _configuration.GetValue<int>("JourneyPlan:BoardingPrepBufferSeconds", 60);
        int transferBuffer = _configuration.GetValue<int>("JourneyPlan:TransferSafetyBufferSeconds", 120);
        
        int targetArrivalTimeSeconds = (int)request.DateTime!.Value.TimeOfDay.TotalSeconds;
        int globalBestDepartureTime = -1; // We want to maximize this

        var activeStops = new HashSet<int>();

        // Initialize Destination Stops (Backwards)
        foreach (var ds in destinationStops)
        {
            if (snapshot.StopIdToIndex.TryGetValue(ds.StopId, out int idx))
            {
                // Must alight here by Target - Walk
                labels[idx].AbsoluteDepartureSeconds = targetArrivalTimeSeconds - ds.WalkingDurationSeconds;
                labels[idx].TotalWalkDurationSeconds = ds.WalkingDurationSeconds;
                labels[idx].TotalWaitDurationSeconds = 0;
                labels[idx].Round = -1;
                activeStops.Add(idx);
                
                if (origStopsSet.Contains(ds.StopId))
                {
                    int originDepTime = labels[idx].AbsoluteDepartureSeconds - prepBuffer;
                    globalBestDepartureTime = Math.Max(globalBestDepartureTime, originDepTime);
                }
            }
        }

        int maxRounds = Math.Min(3, request.MaxTransfers + 1);
        for (int k = 0; k < maxRounds; k++)
        {
            if (activeStops.Count == 0) break;

            var newlyActiveStops = new HashSet<int>();
            var activePatterns = new HashSet<string>();

            // Route Scan: find active patterns
            foreach (var stopIdx in activeStops)
            {
                string sId = snapshot.StopsByIndex[stopIdx].StopId;
                if (snapshot.StopToPatterns.TryGetValue(sId, out var patterns))
                {
                    foreach (var p in patterns)
                        activePatterns.Add(p);
                }
            }

            // Iterate active patterns backwards
            foreach (var patternId in activePatterns)
            {
                var stopsOnPattern = snapshot.PatternToStops[patternId];
                
                int? currentTripIndex = null;
                int? alightedStopIdx = null;
                int currentTripOffset = 0;
                
                for (int i = stopsOnPattern.Count - 1; i >= 0; i--)
                {
                    string stopId = stopsOnPattern[i];
                    if (!snapshot.StopIdToIndex.TryGetValue(stopId, out int stopIdx)) continue;
                    
                    // 1. Can we board the current trip here?
                    if (currentTripIndex.HasValue)
                    {
                        string currentTripId = snapshot.PatternToTrips[patternId][currentTripIndex.Value];
                        var timetable = snapshot.TripTimetables[currentTripId];
                        var boardTime = timetable.First(x => x.StopId == stopId);
                        
                        int departureTime = boardTime.DepartureSeconds + currentTripOffset;
                        int walkTime = labels[alightedStopIdx.Value].TotalWalkDurationSeconds;
                        int waitTime = labels[alightedStopIdx.Value].TotalWaitDurationSeconds; 
                        
                        // Wait time = Departure Time at board stop - Arrival time at alight stop? No.
                        // In ARRIVE_BY, user arrives at alight stop at T_alight. Trip arrives there at trip_arr. Wait time = T_alight - trip_arr.
                        var alightStopTime = timetable.First(x => x.StopId == snapshot.StopsByIndex[alightedStopIdx.Value].StopId);
                        int alightAbsoluteTime = alightStopTime.ArrivalSeconds + currentTripOffset;
                        int additionalWait = labels[alightedStopIdx.Value].AbsoluteDepartureSeconds - alightAbsoluteTime;
                        waitTime += additionalWait;

                        var newLabel = new BackwardRouteLabel
                        {
                            StopId = stopId,
                            StopIndex = stopIdx,
                            AbsoluteDepartureSeconds = departureTime,
                            Round = k,
                            TotalWalkDurationSeconds = walkTime,
                            TotalWaitDurationSeconds = waitTime,
                            NextStopId = snapshot.StopsByIndex[alightedStopIdx.Value].StopId,
                            NextTripId = currentTripId,
                            NextPatternId = patternId,
                            AlightingStopId = snapshot.StopsByIndex[alightedStopIdx.Value].StopId,
                            UsedTransferEdge = false
                        };

                        if (DominatesBackward(newLabel, labels[stopIdx]))
                        {
                            labels[stopIdx] = newLabel;
                            newlyActiveStops.Add(stopIdx);
                            
                            if (origStopsSet.Contains(stopId))
                            {
                                int origDepTime = departureTime - prepBuffer;
                                globalBestDepartureTime = Math.Max(globalBestDepartureTime, origDepTime);
                            }
                        }
                    }

                    // 2. Is this stop active? Can we catch a LATER trip that arrives here <= labels[stopIdx].AbsoluteDepartureSeconds?
                    if (activeStops.Contains(stopIdx))
                    {
                        int targetAlightTime = labels[stopIdx].AbsoluteDepartureSeconds;

                        int bestTripIndex = FindLatestTripIndex(snapshot, patternId, stopId, targetAlightTime, activeServicesToday, activeServicesYesterday, out int offsetSeconds);
                        if (bestTripIndex != -1)
                        {
                            // If we weren't on a trip, or this new trip is a LATER trip (index is greater), we switch!
                            // Note: trips in PatternToTrips are sorted by departure time ascending. So a greater index means a later trip.
                            if (!currentTripIndex.HasValue || bestTripIndex > currentTripIndex.Value)
                            {
                                currentTripIndex = bestTripIndex;
                                alightedStopIdx = stopIdx;
                                currentTripOffset = offsetSeconds;
                            }
                        }
                    }
                }
            }

            // Transfer Relaxation (Backwards)
            var transferActiveStops = new HashSet<int>();
            foreach (var stopIdx in newlyActiveStops)
            {
                string sId = snapshot.StopsByIndex[stopIdx].StopId;
                if (snapshot.StopTransfersReverse.TryGetValue(sId, out var revTransfers))
                {
                    foreach (var tr in revTransfers)
                    {
                        if (snapshot.StopIdToIndex.TryGetValue(tr.FromStopId, out int fromIdx))
                        {
                            // Must arrive at FromStop early enough to walk and transfer
                            int requiredArrivalAtFromStop = labels[stopIdx].AbsoluteDepartureSeconds - tr.WalkingTimeSeconds - transferBuffer;
                            var newLabel = new BackwardRouteLabel
                            {
                                StopId = tr.FromStopId,
                                StopIndex = fromIdx,
                                AbsoluteDepartureSeconds = requiredArrivalAtFromStop,
                                Round = k,
                                TotalWalkDurationSeconds = labels[stopIdx].TotalWalkDurationSeconds + tr.WalkingTimeSeconds,
                                TotalWaitDurationSeconds = labels[stopIdx].TotalWaitDurationSeconds,
                                NextStopId = sId,
                                UsedTransferEdge = true
                            };

                            if (DominatesBackward(newLabel, labels[fromIdx]))
                            {
                                labels[fromIdx] = newLabel;
                                transferActiveStops.Add(fromIdx);
                                
                                if (origStopsSet.Contains(tr.FromStopId))
                                {
                                    int origDepTime = requiredArrivalAtFromStop - prepBuffer;
                                    globalBestDepartureTime = Math.Max(globalBestDepartureTime, origDepTime);
                                }
                            }
                        }
                    }
                }
            }
            activeStops = transferActiveStops;
        }

        // Reconstruction
        var itineraries = new List<ItineraryDto>();
        foreach (var os in originStops)
        {
            if (snapshot.StopIdToIndex.TryGetValue(os.StopId, out int sIdx))
            {
                var lbl = labels[sIdx];
                if (lbl.AbsoluteDepartureSeconds != -1)
                {
                    // Reconstruct forward!
                    var itin = ReconstructBackwardItinerary(snapshot, labels, sIdx, os, destinationStops.FirstOrDefault(d => d.StopId == GetFinalDestinationStop(snapshot, labels, sIdx)), request, targetArrivalTimeSeconds, prepBuffer);
                    if (itin != null) itineraries.Add(itin);
                }
            }
        }

        var simulatedItineraries = new List<ItineraryDto>();
        foreach (var itin in itineraries)
        {
            var firstTransit = itin.Legs.FirstOrDefault(l => l.Mode == "TRANSIT");
            if (firstTransit == null) continue;
            
            var originWalk = itin.Legs.First();
            int departureTimeSeconds = firstTransit.RawGtfsDepartureSeconds!.Value - originWalk.DurationSeconds - prepBuffer;
            
            bool isValid = CascadeSimulateItinerary(itin.Legs, searchDateToday, departureTimeSeconds, prepBuffer, transferBuffer, snapshot, activeServicesToday, activeServicesYesterday, out int finalArrivalSec);
            if (isValid)
            {
                itin.DepartureTime = searchDateToday.AddSeconds(departureTimeSeconds);
                itin.ArrivalTime = searchDateToday.AddSeconds(finalArrivalSec);
                
                // In ARRIVE_BY, we only keep it if the physical arrival is <= requested arrival
                if (itin.ArrivalTime <= request.DateTime!.Value)
                {
                    itin.PlanId = GeneratePlanId(itin, snapshot.FeedHash, "ARRIVE_BY");
                    simulatedItineraries.Add(itin);
                }
            }
        }

        // Sorting: Priority 1: Latest DepartureTime (descending). 
        var finalSorted = simulatedItineraries
            .OrderByDescending(i => i.DepartureTime)
            .ThenBy(i => i.TransferCount)
            .ThenBy(i => i.TotalWalkingTimeSeconds)
            .ThenBy(i => i.TotalWaitingTimeSeconds)
            .ThenBy(i => i.PlanId)
            .ToList();

        return new JourneyPlanSearchResponse { Itineraries = finalSorted, ReasonCode = JourneyPlanResolutionCode.SUCCESS.ToString() };
    }

    private string GetFinalDestinationStop(RoutingSnapshot snapshot, BackwardRouteLabel[] labels, int startIdx)
    {
        int curr = startIdx;
        while(labels[curr].NextStopId != null)
        {
            if (snapshot.StopIdToIndex.TryGetValue(labels[curr].NextStopId, out int nextIdx))
            {
                curr = nextIdx;
            }
            else
            {
                break;
            }
        }
        return labels[curr].StopId;
    }
}


