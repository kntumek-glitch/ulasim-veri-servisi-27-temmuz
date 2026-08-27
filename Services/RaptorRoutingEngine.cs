using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly int _transferPenaltySeconds;
    private readonly double _walkPenaltyMultiplier;

    public RaptorRoutingEngine(
        IRoutingSnapshotManager snapshotManager,
        WalkingRoutingService walkingRoutingService,
        ILogger<RaptorRoutingEngine> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _snapshotManager = snapshotManager;
        _walkingRoutingService = walkingRoutingService;
        _logger = logger;
        _configuration = configuration;
        _transferPenaltySeconds = _configuration.GetValue<int>("JourneyPlan:TransferPenaltySeconds", 300);
        _walkPenaltyMultiplier = _configuration.GetValue<double>("JourneyPlan:WalkPenaltyMultiplier", 1.5);
    }

    
    public async Task<JourneyPlanSearchResponse> SearchJourneyV2Async(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken)
    {
        var telemetry = new ulasim_veri_servisi.Models.Routing.V2TelemetryPayload { SearchMode = request.SearchMode.ToString() };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snapshot = _snapshotManager.GetActiveSnapshot();
            if (snapshot != null)
            {
                telemetry.FeedImportId = snapshot.ActiveImportId;
                telemetry.FeedHash = snapshot.FeedHash;
            }
            
            var response = await SearchJourneyV2AsyncInternal(request, cancellationToken, telemetry);
            
            telemetry.ResultCount = response.Itineraries.Count;
            if (telemetry.ResultCount == 0 && response.ReasonCode == "SUCCESS") telemetry.ReasonCode = "NO_ROUTE_FOUND";
            else telemetry.ReasonCode = response.ReasonCode;
            
            if (request.IncludeIntermediateStops && response.Itineraries.Any() && snapshot != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var mapper = scope.ServiceProvider.GetRequiredService<ulasim_veri_servisi.Services.JourneyPlanning.Mapping.IJourneyResultMapper>();
                var trtOffset = TimeSpan.FromHours(3);
                var tzi = TimeZoneInfo.CreateCustomTimeZone("TRT", trtOffset, "TRT", "TRT");
                await mapper.PopulateIntermediateStopsAsync(response.Itineraries, tzi, snapshot.ActiveImportId, cancellationToken);
            }
            
            foreach (var it in response.Itineraries)
            {
                foreach (var leg in it.Legs)
                {
                    if (leg.Mode == "WALK")
                    {
                        if (leg.IsApproximate) telemetry.ApproximateWalkingLegCount++;
                        else telemetry.ExactWalkingLegCount++;
                    }
                }
            }
            if (response != null)
            {
                response.Metadata ??= new JourneyPlanMetadataDto();
                response.Metadata.CalculationDurationMs = sw.ElapsedMilliseconds;
                response.Metadata.SearchMode = request.SearchMode.ToString();
                
                if (snapshot != null)
                {
                    response.Metadata.ActiveImportId = snapshot.ActiveImportId;
                    response.Metadata.FeedHash = snapshot.FeedHash;
                    response.Metadata.SnapshotCreatedAt = snapshot.CreatedAt.ToString("o");
                    response.Metadata.IsFeedStale = request.DateTime?.Date > snapshot.FeedValidTo.Date;
                }
            }
            return response;
        }
        catch(Exception ex)
        {
            telemetry.ReasonCode = ex.GetType().Name;
            throw;
        }
        finally
        {
            sw.Stop();
            telemetry.CalculationDurationMs = sw.ElapsedMilliseconds;
            _logger.LogInformation("V2 Routing Telemetry: {Telemetry}", System.Text.Json.JsonSerializer.Serialize(telemetry));
        }
    }

    public async Task<JourneyPlanSearchResponse> SearchArriveByJourneyV2Async(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken)
    {
        var telemetry = new ulasim_veri_servisi.Models.Routing.V2TelemetryPayload { SearchMode = request.SearchMode.ToString() };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var snapshot = _snapshotManager.GetActiveSnapshot();
            if (snapshot != null)
            {
                telemetry.FeedImportId = snapshot.ActiveImportId;
                telemetry.FeedHash = snapshot.FeedHash;
            }
            
            var response = await SearchArriveByJourneyV2AsyncInternal(request, cancellationToken, telemetry);
            
            telemetry.ResultCount = response.Itineraries.Count;
            if (telemetry.ResultCount == 0 && response.ReasonCode == "SUCCESS") telemetry.ReasonCode = "NO_ROUTE_FOUND";
            else telemetry.ReasonCode = response.ReasonCode;
            
            foreach (var it in response.Itineraries)
            {
                foreach (var leg in it.Legs)
                {
                    if (leg.Mode == "WALK")
                    {
                        if (leg.IsApproximate) telemetry.ApproximateWalkingLegCount++;
                        else telemetry.ExactWalkingLegCount++;
                    }
                }
            }
            if (response != null)
            {
                response.Metadata ??= new JourneyPlanMetadataDto();
                response.Metadata.CalculationDurationMs = sw.ElapsedMilliseconds;
                response.Metadata.SearchMode = request.SearchMode.ToString();
                
                if (snapshot != null)
                {
                    response.Metadata.ActiveImportId = snapshot.ActiveImportId;
                    response.Metadata.FeedHash = snapshot.FeedHash;
                    response.Metadata.SnapshotCreatedAt = snapshot.CreatedAt.ToString("o");
                    response.Metadata.IsFeedStale = request.DateTime?.Date > snapshot.FeedValidTo.Date;
                }
            }
            return response;
        }
        catch(Exception ex)
        {
            telemetry.ReasonCode = ex.GetType().Name;
            throw;
        }
        finally
        {
            sw.Stop();
            telemetry.CalculationDurationMs = sw.ElapsedMilliseconds;
            _logger.LogInformation("V2 Routing Telemetry: {Telemetry}", System.Text.Json.JsonSerializer.Serialize(telemetry));
        }
    }

    private async Task<JourneyPlanSearchResponse> SearchJourneyV2AsyncInternal(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken, ulasim_veri_servisi.Models.Routing.V2TelemetryPayload telemetry)
    {
        var snapshot = _snapshotManager.GetActiveSnapshot();
        if (snapshot == null)
        {
            _logger.LogWarning("Routing snapshot is not available. Search cannot be performed.");
            throw new SnapshotUnavailableException("Routing graph is not loaded or is currently updating.");
        }

        var trtOffset = TimeSpan.FromHours(3);
        var requestTimeTrt = request.DateTime!.Value.ToOffset(trtOffset);
        DateTime searchDate = requestTimeTrt.Date;
        
        if (searchDate < snapshot.FeedValidFrom.Date || searchDate > snapshot.FeedValidTo.Date)
        {
            return new JourneyPlanSearchResponse { ReasonCode = "FEED_STALE" };
        }

        if (request.SearchMode == RoutingMode.ARRIVE_BY)
        {
            return await SearchArriveByJourneyV2AsyncInternal(request, cancellationToken, telemetry);
        }
        else if (request.SearchMode != RoutingMode.DEPART_AT)
        {
            throw new NotImplementedException("Only DEPART_AT and ARRIVE_BY modes are supported by the RAPTOR engine.");
        }

        // 1. Array allocation (RouteLabel[])
        int numStops = snapshot.StopsByIndex.Length;
        int maxRounds = Math.Min(3, request.MaxTransfers + 1);
        var labels = new RouteLabel[maxRounds + 1][];
        for (int r = 0; r <= maxRounds; r++)
        {
            labels[r] = new RouteLabel[numStops];
            for (int i = 0; i < numStops; i++)
            {
                labels[r][i] = new RouteLabel
                {
                    StopId = snapshot.StopsByIndex[i].StopId,
                    StopIndex = i,
                    AbsoluteArrivalSeconds = int.MaxValue,
                    Round = -1,
                    TotalWalkDurationSeconds = int.MaxValue,
                    TotalWaitDurationSeconds = int.MaxValue
                };
            }
        }

        // 2. Find nearby origin stops (Walking)
        var originStops = FindNearbyStops(snapshot, request.Origin.Lat, request.Origin.Lon, request.MaxWalkingMeters);
        telemetry.OriginCandidateStopCount = originStops.Count;
        if (!originStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified origin walking radius.", true);
        
        var destinationStops = FindNearbyStops(snapshot, request.Destination.Lat, request.Destination.Lon, request.MaxWalkingMeters);
        telemetry.DestinationCandidateStopCount = destinationStops.Count;
        if (!destinationStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified destination walking radius.", false);

        var destStopsSet = destinationStops.Select(s => s.StopId).ToHashSet();
        var destStopsWalkTime = destinationStops.ToDictionary(s => s.StopId, s => s.WalkingDurationSeconds);
        
        DateTime searchDateToday = requestTimeTrt.Date;
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
            return new JourneyPlanSearchResponse { ReasonCode = "NO_ACTIVE_SERVICE" };
        }
        
        int prepBuffer = _configuration.GetValue<int>("JourneyPlan:BoardingPrepBufferSeconds", 60);
        int transferBuffer = _configuration.GetValue<int>("JourneyPlan:TransferSafetyBufferSeconds", 120);
        
        int departureTimeSeconds = (int)requestTimeTrt.TimeOfDay.TotalSeconds;
        int globalBestArrivalTime = int.MaxValue;

        var activeStops = new HashSet<int>();

        // Initialize Origin Stops
        foreach (var os in originStops)
        {
            if (snapshot.StopIdToIndex.TryGetValue(os.StopId, out int idx))
            {
                labels[0][idx].AbsoluteArrivalSeconds = departureTimeSeconds + os.WalkingDurationSeconds + prepBuffer;
                labels[0][idx].TotalWalkDurationSeconds = os.WalkingDurationSeconds;
                labels[0][idx].TotalWaitDurationSeconds = 0;
                labels[0][idx].Round = -1;
                activeStops.Add(idx);
                
                if (destStopsWalkTime.TryGetValue(os.StopId, out int finalWalk1))
                {
                    globalBestArrivalTime = Math.Min(globalBestArrivalTime, labels[0][idx].AbsoluteArrivalSeconds + finalWalk1);
                }
            }
        }

        // RAPTOR Loop
        for (int k = 1; k <= maxRounds; k++)
        {
            telemetry.RoundCount++;
            if (activeStops.Count == 0) break; // Pruning condition

            var newlyActiveStops = new HashSet<int>();
            var activePatterns = new HashSet<string>();

            // Route Scan: find active patterns
            foreach (var stopIdx in activeStops)
            {
                if (labels[k - 1][stopIdx].AbsoluteArrivalSeconds >= globalBestArrivalTime)
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
                telemetry.PatternScannedCount++;
                // In a real RAPTOR, we board at the earliest active stop and propagate.
                // For simplicity, we just evaluate all trip departures from active stops on this pattern.
                var stopsOnPattern = snapshot.PatternToStops[patternId];
                
                int? currentTripIndex = null;
                int? boardedStopIdx = null;
                int? boardedPatternIndex = null;
                int currentTripOffset = 0;
                
                for (int i = 0; i < stopsOnPattern.Count; i++)
                {
                    string stopId = stopsOnPattern[i];
                    if (!snapshot.StopIdToIndex.TryGetValue(stopId, out int stopIdx)) continue;
                    
                    if (currentTripIndex.HasValue)
                    {
                        // We are on a trip, we can disembark here
                        string currentTripId = snapshot.PatternToTrips[patternId][currentTripIndex.Value];
                        var timetable = snapshot.TripTimetables[currentTripId];
                        var stopTime = timetable[i];
                        
                        int arrivalTime = stopTime.ArrivalSeconds + currentTripOffset;
                        int walkTime = labels[k - 1][boardedStopIdx.Value].TotalWalkDurationSeconds;
                        int waitTime = labels[k - 1][boardedStopIdx.Value].TotalWaitDurationSeconds; 
                        
                        // Wait time = Departure Time from boarded stop - Arrival time at boarded stop
                        var boardStopTime = timetable[boardedPatternIndex.Value];
                        int boardAbsoluteTime = boardStopTime.DepartureSeconds + currentTripOffset;
                        int additionalWait = boardAbsoluteTime - labels[k - 1][boardedStopIdx.Value].AbsoluteArrivalSeconds;
                        waitTime += additionalWait;

                        // Prevent 0-minute or extremely short meaningless micro-legs (terminal crawls)
                        if (arrivalTime - boardAbsoluteTime <= 120)
                        {
                            // Skip disembarking, it's not a useful transit leg
                        }
                        else
                        {
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
                                BoardingStopPatternIndex = boardedPatternIndex.Value,
                                AlightingStopPatternIndex = i,
                                UsedTransferEdge = false
                            };

                            if (Dominates(newLabel, labels[k][stopIdx]))
                            {
                                telemetry.LabelUpdateCount++;
                                labels[k][stopIdx] = newLabel;
                                newlyActiveStops.Add(stopIdx);
                                
                                if (destStopsWalkTime.TryGetValue(stopId, out int finalWalk2))
                                {
                                    globalBestArrivalTime = Math.Min(globalBestArrivalTime, arrivalTime + finalWalk2);
                                }
                            }
                        }
                    }

                    // Can we board here or find an earlier trip?
                    if (activeStops.Contains(stopIdx) && labels[k - 1][stopIdx].AbsoluteArrivalSeconds < globalBestArrivalTime)
                    {
                        // EBT: Must wait for Boarding/Transfer buffer
                        int ebt = labels[k - 1][stopIdx].AbsoluteArrivalSeconds;
                        if (labels[k - 1][stopIdx].Round == -1) ebt += prepBuffer;
                        else if (labels[k - 1][stopIdx].Round >= 0)
                        {
                            if (snapshot.StopTransfers.TryGetValue(stopId, out var selfTransfers))
                            {
                                var st = selfTransfers.FirstOrDefault(x => x.ToStopId == stopId);
                                if (st != null) ebt += st.WalkingTimeSeconds + transferBuffer;
                                else ebt += transferBuffer;
                            }
                            else ebt += transferBuffer;
                        }
                        int bestTripIndex = FindEarliestTripIndex(snapshot, patternId, i, ebt, activeServicesToday, activeServicesYesterday, out int offsetSeconds);
                        telemetry.TripScannedCount++;
                        if (bestTripIndex != -1)
                        {
                            if (!currentTripIndex.HasValue || bestTripIndex < currentTripIndex.Value)
                            {
                                currentTripIndex = bestTripIndex;
                                boardedStopIdx = stopIdx;
                                boardedPatternIndex = i;
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
                        telemetry.TransferRelaxationCount++;
                        if (snapshot.StopIdToIndex.TryGetValue(tr.ToStopId, out int toIdx))
                        {
                            // Apply transfer safety buffer here
                            int arrival = labels[k][stopIdx].AbsoluteArrivalSeconds + tr.WalkingTimeSeconds + transferBuffer;
                            var newLabel = new RouteLabel
                            {
                                StopId = tr.ToStopId,
                                StopIndex = toIdx,
                                AbsoluteArrivalSeconds = arrival,
                                Round = k,
                                TotalWalkDurationSeconds = labels[k][stopIdx].TotalWalkDurationSeconds + tr.WalkingTimeSeconds,
                                TotalWaitDurationSeconds = labels[k][stopIdx].TotalWaitDurationSeconds,
                                PreviousStopId = sId,
                                UsedTransferEdge = true
                            };

                            if (Dominates(newLabel, labels[k][toIdx]))
                            {
                                telemetry.LabelUpdateCount++;
                                labels[k][toIdx] = newLabel;
                                transferActiveStops.Add(toIdx);
                                
                                if (destStopsWalkTime.TryGetValue(tr.ToStopId, out int finalWalk3))
                                {
                                    globalBestArrivalTime = Math.Min(globalBestArrivalTime, arrival + finalWalk3);
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
        
        // Pre-filter and sort destination stops to avoid reconstructing hundreds of 
        // suboptimal itineraries which causes OSRM API timeouts.
        var validDestStopsList = new List<dynamic>();
        foreach (var d in destinationStops)
        {
            if (!snapshot.StopIdToIndex.TryGetValue(d.StopId, out int idx)) continue;
            for (int r = 1; r <= maxRounds; r++)
            {
                if (labels[r][idx].AbsoluteArrivalSeconds != int.MaxValue)
                {
                    validDestStopsList.Add(new { Stop = d, Index = idx, Label = labels[r][idx], FinalRound = r });
                }
            }
        }
        var validDestStops = validDestStopsList
            .OrderBy(x => x.Label.AbsoluteArrivalSeconds + x.Stop.WalkingDurationSeconds)
            .Take(20)
            .ToList();

        foreach (var destData in validDestStops)
        {
            var destStop = destData.Stop;
            int destIdx = destData.Index;
            var finalLabel = destData.Label;
            int currRound = destData.FinalRound;
            
            // Reconstruct backward
            var legs = new List<LegDto>();
            int currIdx = destIdx;
            
            var visited = new HashSet<int>();
            while (currIdx != -1)
            {
                if (!visited.Add(currIdx)) break; // Cycle detected
                var curr = labels[currRound][currIdx];
                if (curr.Round == -1) // Reached origin
                {
                    var originWalk = originStops.FirstOrDefault(x => x.StopId == curr.StopId);
                    if (originWalk != null)
                    {
                        var wr = await _walkingRoutingService.CalculateWalkingRouteAsync(
                            request.Origin.Lat, request.Origin.Lon, 
                            snapshot.StopsByIndex[currIdx].StopLat, snapshot.StopsByIndex[currIdx].StopLon, 
                            request.IncludeWalkingGeometry, "foot", cancellationToken);
                            
                        legs.Add(new LegDto
                        {
                            Mode = "WALK",
                            DurationSeconds = wr.State.IsSuccess ? (int)wr.DurationSeconds : originWalk.WalkingDurationSeconds,
                            DistanceMeters = wr.State.IsSuccess ? (int)wr.DistanceMeters : 0,
                            GeometryGeoJson = wr.GeometryGeoJson,
                            FromStopName = "Origin",
                            FromStopLat = request.Origin.Lat,
                            FromStopLon = request.Origin.Lon,
                            ToStopId = curr.StopId,
                            ToStopName = snapshot.StopsByIndex[currIdx].StopName,
                            ToStopLat = snapshot.StopsByIndex[currIdx].StopLat,
                            ToStopLon = snapshot.StopsByIndex[currIdx].StopLon,
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
                    
                    var transferWalkLeg = new LegDto
                    {
                        Mode = "WALK",
                        DurationSeconds = transfer.WalkingTimeSeconds,
                        DistanceMeters = transfer.DistanceMeters,
                        FromStopId = curr.PreviousStopId,
                        FromStopName = snapshot.StopsByIndex[prevIdx].StopName,
                        FromStopLat = snapshot.StopsByIndex[prevIdx].StopLat,
                        FromStopLon = snapshot.StopsByIndex[prevIdx].StopLon,
                        ToStopId = curr.StopId,
                        ToStopName = snapshot.StopsByIndex[currIdx].StopName,
                        ToStopLat = snapshot.StopsByIndex[currIdx].StopLat,
                        ToStopLon = snapshot.StopsByIndex[currIdx].StopLon,
                        WalkingSource = "Haversine",
                        IsApproximate = true,
                        WalkingWarning = "Station-to-station static transfer",
                        HasGeometry = false
                    };
                    
                    var wrTransfer = await _walkingRoutingService.CalculateWalkingRouteAsync(
                        transferWalkLeg.FromStopLat.Value, transferWalkLeg.FromStopLon.Value,
                        transferWalkLeg.ToStopLat.Value, transferWalkLeg.ToStopLon.Value,
                        request.IncludeWalkingGeometry, "foot", cancellationToken);
                        
                    if (wrTransfer.State.IsSuccess)
                    {
                        transferWalkLeg.DistanceMeters = (int)wrTransfer.DistanceMeters;
                        transferWalkLeg.DurationSeconds = (int)wrTransfer.DurationSeconds;
                        transferWalkLeg.GeometryGeoJson = wrTransfer.GeometryGeoJson;
                        transferWalkLeg.HasGeometry = wrTransfer.GeometryGeoJson != null;
                        transferWalkLeg.IsApproximate = false;
                        transferWalkLeg.WalkingSource = "OSRM";
                        transferWalkLeg.WalkingWarning = null;
                    }
                    
                    legs.Add(transferWalkLeg);
                    currIdx = prevIdx;
                    // currRound stays the same for walk transfers
                }
                else
                {
                    var boardIdx = snapshot.StopIdToIndex[curr.BoardingStopId];
                    var boardTime = snapshot.TripTimetables[curr.PreviousTripId][curr.BoardingStopPatternIndex];
                    var alightTime = snapshot.TripTimetables[curr.PreviousTripId][curr.AlightingStopPatternIndex];
                    
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
                        FromStopPatternIndex = curr.BoardingStopPatternIndex,
                        FromStopName = snapshot.StopsByIndex[boardIdx].StopName,
                        FromStopLat = snapshot.StopsByIndex[boardIdx].StopLat,
                        FromStopLon = snapshot.StopsByIndex[boardIdx].StopLon,
                        ToStopId = curr.StopId,
                        ToStopPatternIndex = curr.AlightingStopPatternIndex,
                        ToStopName = snapshot.StopsByIndex[currIdx].StopName,
                        ToStopLat = snapshot.StopsByIndex[currIdx].StopLat,
                        ToStopLon = snapshot.StopsByIndex[currIdx].StopLon,
                        RawGtfsDepartureSeconds = boardTime.DepartureSeconds,
                        RawGtfsArrivalSeconds = alightTime.ArrivalSeconds,
                        DurationSeconds = alightTime.ArrivalSeconds - boardTime.DepartureSeconds
                    });
                    currIdx = boardIdx;
                    currRound--;
                }
            }
            
            legs.Reverse();
            
            // Final Walk (Door-to-Door)
            var wrDest = await _walkingRoutingService.CalculateWalkingRouteAsync(
                snapshot.StopsByIndex[destIdx].StopLat, snapshot.StopsByIndex[destIdx].StopLon,
                request.Destination.Lat, request.Destination.Lon, request.IncludeWalkingGeometry, "foot", cancellationToken);
                
            int actualFinalWalkDuration = wrDest.State.IsSuccess ? (int)wrDest.DurationSeconds : destStop.WalkingDurationSeconds;
            
            legs.Add(new LegDto
            {
                Mode = "WALK",
                DurationSeconds = actualFinalWalkDuration,
                DistanceMeters = wrDest.State.IsSuccess ? (int)wrDest.DistanceMeters : 0,
                GeometryGeoJson = wrDest.GeometryGeoJson,
                FromStopId = destStop.StopId,
                FromStopName = snapshot.StopsByIndex[destIdx].StopName,
                FromStopLat = snapshot.StopsByIndex[destIdx].StopLat,
                FromStopLon = snapshot.StopsByIndex[destIdx].StopLon,
                ToStopName = "Destination",
                ToStopLat = request.Destination.Lat,
                ToStopLon = request.Destination.Lon,
                WalkingSource = wrDest.State.IsSuccess ? "OSRM" : "Haversine",
                IsApproximate = !wrDest.State.IsSuccess,
                WalkingWarning = wrDest.State.ErrorMessage,
                HasGeometry = wrDest.GeometryGeoJson != null
            });
            
            // Forward Cascade Simulation!
            bool isValid = CascadeSimulateItinerary(legs, searchDateToday, departureTimeSeconds, prepBuffer, transferBuffer, snapshot, activeServicesToday, activeServicesYesterday, out int finalArrivalTimeSeconds, trtOffset);
            if (!isValid) continue; // Itinerary broken by OSRM delay

            bool isApprox = legs.Any(l => l.Mode == "WALK" && l.IsApproximate);
            
            DateTimeOffset midnightTrt = new DateTimeOffset(searchDateToday.Year, searchDateToday.Month, searchDateToday.Day, 0, 0, 0, trtOffset);

            var iti = new ItineraryDto
            {
                Legs = legs,
                TotalWalkingDistanceMeters = legs.Where(l => l.Mode == "WALK").Sum(l => l.DistanceMeters),
                TotalWalkingTimeSeconds = legs.Where(l => l.Mode == "WALK").Sum(l => l.DurationSeconds),
                TransferCount = legs.Count(l => l.Mode == "TRANSIT") - 1, // Recalculate based on actual transit legs
                TotalWaitingTimeSeconds = 0, // Cascade simulator doesn't easily compute this, can be derived later if needed
                TotalInVehicleTimeSeconds = legs.Where(l => l.Mode == "TRANSIT").Sum(l => l.DurationSeconds),
                ArrivalTime = midnightTrt.AddSeconds(finalArrivalTimeSeconds),
                DepartureTime = midnightTrt.AddSeconds(departureTimeSeconds),
                IsApproximate = isApprox
            };
            
            iti.PlanId = GeneratePlanId(iti, snapshot.FeedHash, request.SearchMode.ToString());
            
            itineraries.Add(iti);
        }

        // Global Route Sorting Hierarchy and Diversity
        // Group itineraries by the sequence of RouteIds to ensure diverse alternatives
        // We use an EffectiveArrivalTime to penalize long walks. If a route saves 20 mins but requires a 25 min walk, it's penalized.
        // Penalty = 1.5x walking time (in seconds).
        itineraries = itineraries
            .GroupBy(x => string.Join("|", x.Legs.Where(l => l.Mode == "TRANSIT").Select(l => l.RouteId ?? "WALK")))
            .Select(g => g.OrderBy(x => x.ArrivalTime.AddSeconds(x.TotalWalkingTimeSeconds * 1.5)) // Within the same route combination, pick the one with best EffectiveArrivalTime
                          .First())
            .OrderBy(x => x.ArrivalTime.AddSeconds(x.TotalWalkingTimeSeconds * 1.5)) // Priority 1: Best EffectiveArrivalTime
            .ThenBy(x => x.TransferCount) // Priority 2: Least Transfer Count
            .ThenBy(x => x.TotalWalkingTimeSeconds) // Priority 3: Least Walk
            .ThenBy(x => x.TotalWaitingTimeSeconds) // Priority 4: Least Wait
            .Take(request.MaxResults)
            .ToList();

        foreach (var iti in itineraries)
        {
            iti.Fares = FareCalculatorService.CalculateFares(iti);
        }

        return new JourneyPlanSearchResponse { Itineraries = itineraries };
    }

    private bool CascadeSimulateItinerary(List<LegDto> legs, DateTime searchDate, int currentTimeSeconds, int prepBuffer, int transferBuffer, RoutingSnapshot snapshot, HashSet<string> activeToday, HashSet<string> activeYesterday, out int finalArrivalTimeSeconds, TimeSpan trtOffset)
    {
        finalArrivalTimeSeconds = currentTimeSeconds;
        DateTimeOffset midnight = new DateTimeOffset(searchDate.Year, searchDate.Month, searchDate.Day, 0, 0, 0, trtOffset);

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
                int tEbt = currentTimeSeconds;
                if (i > 0 && legs[i-1].Mode == "TRANSIT")
                {
                    if (snapshot.StopTransfers.TryGetValue(leg.FromStopId!, out var selfTransfers))
                    {
                        var st = selfTransfers.FirstOrDefault(x => x.ToStopId == leg.FromStopId);
                        if (st != null) tEbt += st.WalkingTimeSeconds + transferBuffer;
                        else tEbt += transferBuffer;
                    }
                    else tEbt += transferBuffer;
                }
                int bestTripIdx = FindEarliestTripIndex(snapshot, leg.PatternId!, leg.FromStopPatternIndex, tEbt, activeToday, activeYesterday, out int offsetSeconds);
                if (bestTripIdx == -1) return false;

                string tripId = snapshot.PatternToTrips[leg.PatternId!][bestTripIdx];
                var timetable = snapshot.TripTimetables[tripId];
                var boardTime = timetable[leg.FromStopPatternIndex];
                var alightTime = timetable[leg.ToStopPatternIndex];

                leg.TripId = tripId;
                leg.RawGtfsDepartureSeconds = boardTime.DepartureSeconds;
                leg.RawGtfsArrivalSeconds = alightTime.ArrivalSeconds;
                leg.DurationSeconds = alightTime.ArrivalSeconds - boardTime.DepartureSeconds;
                
                int absDeparture = boardTime.DepartureSeconds + offsetSeconds;
                int absArrival = alightTime.ArrivalSeconds + offsetSeconds;

                leg.DepartureTime = midnight.AddSeconds(absDeparture);
                leg.ArrivalTime = midnight.AddSeconds(absArrival);

                if (absArrival < absDeparture)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"NEGATIVE DURATION DETECTED! Trip: {tripId}");
                    sb.AppendLine($"From: {leg.FromStopName} (idx {leg.FromStopPatternIndex}) at {leg.DepartureTime}");
                    sb.AppendLine($"To: {leg.ToStopName} (idx {leg.ToStopPatternIndex}) at {leg.ArrivalTime}");
                    for (int j = 0; j < timetable.Count; j++)
                    {
                        sb.AppendLine($"  [{j}] StopId: {timetable[j].StopId}, Arr: {timetable[j].ArrivalSeconds}, Dep: {timetable[j].DepartureSeconds}");
                    }
                    _logger.LogError(sb.ToString());
                }

                currentTimeSeconds = absArrival;
            }
        }
        
        finalArrivalTimeSeconds = currentTimeSeconds;
        return true;
    }

    private bool Dominates(RouteLabel newLabel, RouteLabel oldLabel)
    {
        if (oldLabel.AbsoluteArrivalSeconds == int.MaxValue) return true;

        long newPenalty = newLabel.Round > 0 ? newLabel.Round * _transferPenaltySeconds : 0;
        long oldPenalty = oldLabel.Round > 0 ? oldLabel.Round * _transferPenaltySeconds : 0;
        
        long newWalk = (long)(newLabel.TotalWalkDurationSeconds * _walkPenaltyMultiplier);
        long oldWalk = (long)(oldLabel.TotalWalkDurationSeconds * _walkPenaltyMultiplier);

        long newScore = (long)newLabel.AbsoluteArrivalSeconds + newPenalty + newWalk;
        long oldScore = (long)oldLabel.AbsoluteArrivalSeconds + oldPenalty + oldWalk;

        if (newScore < oldScore) return true;
        if (newScore > oldScore) return false;

        return newLabel.TotalWaitDurationSeconds < oldLabel.TotalWaitDurationSeconds;
    }

        private int FindEarliestTripIndex(RoutingSnapshot snapshot, string patternId, int patternIndex, int ebt, HashSet<string> activeToday, HashSet<string> activeYesterday, out int offsetSeconds)
    {
        var trips = snapshot.PatternToTrips[patternId];
        offsetSeconds = 0;
        
        string pKey = $"{patternIndex}_{patternId}";
        if (!snapshot.PatternStopDepartureIndices.TryGetValue(pKey, out var sortedIndices)) return -1;
        
        int FindValidTrip(int targetTime, HashSet<string> activeSet)
        {
            int low = 0;
            int high = trips.Count - 1;
            int bestArrIdx = -1;
            
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                int originalIndex = sortedIndices[mid];
                string tId = trips[originalIndex];
                
                var st = snapshot.TripTimetables[tId][patternIndex];
                
                if (st.DepartureSeconds >= targetTime)
                {
                    bestArrIdx = mid;
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }
            
            if (bestArrIdx != -1)
            {
                for (int i = bestArrIdx; i < sortedIndices.Length; i++)
                {
                    int originalIndex = sortedIndices[i];
                    string tId = trips[originalIndex];
                    string sId = snapshot.TripToServiceId[tId];
                    if (activeSet.Contains(sId)) return originalIndex;
                }
            }
            return -1;
        }
        
        int bestTodayIdx = FindValidTrip(ebt, activeToday);
        int bestYesterdayIdx = FindValidTrip(ebt + 86400, activeYesterday);
        
        int bestTodayDep = bestTodayIdx != -1 ? snapshot.TripTimetables[trips[bestTodayIdx]][patternIndex].DepartureSeconds : int.MaxValue;
        int bestYesterdayDep = bestYesterdayIdx != -1 ? snapshot.TripTimetables[trips[bestYesterdayIdx]][patternIndex].DepartureSeconds - 86400 : int.MaxValue;
        
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
        var allNearby = new List<(string StopId, double Distance)>();
        for (int i = 0; i < snapshot.StopsByIndex.Length; i++)
        {
            var stop = snapshot.StopsByIndex[i];
            double dist = GetHaversineDistance(lat, lon, stop.StopLat, stop.StopLon);
            if (dist <= maxMeters)
            {
                allNearby.Add((stop.StopId, dist));
            }
        }
        
        if (!allNearby.Any()) return new List<LocalWalkEdge>();

        // Sort by distance and limit candidate stops to prevent combinatorial explosion 
        // which causes severe thread pool starvation for large maxWalkingMeters
        int maxCandidates = _configuration.GetValue<int>("JourneyPlan:MaxCandidateStops", 15);
        allNearby = allNearby.OrderBy(x => x.Distance).Take(maxCandidates).ToList();

        var result = new List<LocalWalkEdge>();
        foreach(var item in allNearby)
        {
            result.Add(new LocalWalkEdge
            {
                StopId = item.StopId,
                WalkingDurationSeconds = (int)(item.Distance / 1.4) // 1.4 m/s walking speed
            });
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
        if (oldLabel.AbsoluteDepartureSeconds == -1) return true;

        long newPenalty = newLabel.Round > 0 ? newLabel.Round * _transferPenaltySeconds : 0;
        long oldPenalty = oldLabel.Round > 0 ? oldLabel.Round * _transferPenaltySeconds : 0;

        long newWalk = (long)(newLabel.TotalWalkDurationSeconds * _walkPenaltyMultiplier);
        long oldWalk = (long)(oldLabel.TotalWalkDurationSeconds * _walkPenaltyMultiplier);

        long newScore = (long)newLabel.AbsoluteDepartureSeconds - newPenalty - newWalk;
        long oldScore = (long)oldLabel.AbsoluteDepartureSeconds - oldPenalty - oldWalk;

        if (newScore > oldScore) return true;
        if (newScore < oldScore) return false;

        return newLabel.TotalWaitDurationSeconds < oldLabel.TotalWaitDurationSeconds;
    }

        private int FindLatestTripIndex(RoutingSnapshot snapshot, string patternId, int patternIndex, int targetAlightTime, HashSet<string> activeToday, HashSet<string> activeYesterday, out int offsetSeconds)
    {
        var trips = snapshot.PatternToTrips[patternId];
        offsetSeconds = 0;
        
        string pKey = $"{patternIndex}_{patternId}";
        if (!snapshot.PatternStopArrivalIndices.TryGetValue(pKey, out var sortedIndices)) return -1;
        
        int FindValidTrip(int targetArrival, HashSet<string> activeSet)
        {
            int low = 0;
            int high = trips.Count - 1;
            int bestArrIdx = -1;
            
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                int originalIndex = sortedIndices[mid];
                string tId = trips[originalIndex];
                
                var st = snapshot.TripTimetables[tId][patternIndex];
                
                if (st.ArrivalSeconds <= targetArrival)
                {
                    bestArrIdx = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            
            if (bestArrIdx != -1)
            {
                for (int i = bestArrIdx; i >= 0; i--)
                {
                    int originalIndex = sortedIndices[i];
                    string tId = trips[originalIndex];
                    string sId = snapshot.TripToServiceId[tId];
                    if (activeSet.Contains(sId)) return originalIndex;
                }
            }
            return -1;
        }
        
        int bestTodayIdx = FindValidTrip(targetAlightTime, activeToday);
        int bestYesterdayIdx = FindValidTrip(targetAlightTime + 86400, activeYesterday);
        
        int bestTodayArr = bestTodayIdx != -1 ? snapshot.TripTimetables[trips[bestTodayIdx]][patternIndex].ArrivalSeconds : -1;
        int bestYesterdayArr = bestYesterdayIdx != -1 ? snapshot.TripTimetables[trips[bestYesterdayIdx]][patternIndex].ArrivalSeconds - 86400 : -1;
        
        if (bestTodayIdx != -1 && bestYesterdayIdx != -1)
        {
            if (bestYesterdayArr > bestTodayArr)
            {
                offsetSeconds = -86400;
                return bestYesterdayIdx;
            }
            offsetSeconds = 0;
            return bestTodayIdx;
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
    private async Task<ItineraryDto?> ReconstructBackwardItinerary(
        RoutingSnapshot snapshot, 
        BackwardRouteLabel[][] labels, int finalRound, 
        int originStopIdx, 
        LocalWalkEdge originWalk, 
        LocalWalkEdge destWalk, 
        JourneyPlanV2SearchRequest request, 
        int targetArrivalTimeSeconds, 
        int prepBuffer,
        CancellationToken cancellationToken)
    {
        var legs = new List<LegDto>();
        int currIdx = originStopIdx;
        int currRound = finalRound;
        
        // Origin walk is added later by CascadeSimulator or we can add it here as Haversine fallback.
        // Actually Cascade simulator expects all legs to be present, and it will re-evaluate them.
        
        legs.Add(new LegDto
        {
            Mode = "WALK",
            DurationSeconds = originWalk.WalkingDurationSeconds,
            DistanceMeters = (int)(originWalk.WalkingDurationSeconds * 1.4),
            FromStopName = "Origin",
            FromStopLat = request.Origin.Lat,
            FromStopLon = request.Origin.Lon,
            ToStopId = snapshot.StopsByIndex[currIdx].StopId,
            ToStopName = snapshot.StopsByIndex[currIdx].StopName,
            ToStopLat = snapshot.StopsByIndex[currIdx].StopLat,
            ToStopLon = snapshot.StopsByIndex[currIdx].StopLon,
            WalkingSource = "Haversine",
            IsApproximate = true,
            HasGeometry = false
        });
        
        var visited = new HashSet<int>();
        while (currIdx != -1)
        {
            if (!visited.Add(currIdx)) break; // Cycle detected
            var curr = labels[currRound][currIdx];
            if (curr.Round == -1) // Reached destination
            {
                if (destWalk != null)
                {
                    var destWalkLeg = new LegDto
                    {
                        Mode = "WALK",
                        DurationSeconds = destWalk.WalkingDurationSeconds,
                        DistanceMeters = (int)(destWalk.WalkingDurationSeconds * 1.4),
                        FromStopId = curr.StopId,
                        FromStopName = snapshot.StopsByIndex[currIdx].StopName,
                        FromStopLat = snapshot.StopsByIndex[currIdx].StopLat,
                        FromStopLon = snapshot.StopsByIndex[currIdx].StopLon,
                        ToStopName = "Destination",
                        ToStopLat = request.Destination.Lat,
                        ToStopLon = request.Destination.Lon,
                        WalkingSource = "Haversine",
                        IsApproximate = true,
                        HasGeometry = false
                    };
                    
                    var wrDest = await _walkingRoutingService.CalculateWalkingRouteAsync(
                        destWalkLeg.FromStopLat.Value, destWalkLeg.FromStopLon.Value,
                        destWalkLeg.ToStopLat.Value, destWalkLeg.ToStopLon.Value,
                        request.IncludeWalkingGeometry, "foot", cancellationToken);
                        
                    if (wrDest.State.IsSuccess)
                    {
                        destWalkLeg.DistanceMeters = (int)wrDest.DistanceMeters;
                        destWalkLeg.DurationSeconds = (int)wrDest.DurationSeconds;
                        destWalkLeg.GeometryGeoJson = wrDest.GeometryGeoJson;
                        destWalkLeg.HasGeometry = wrDest.GeometryGeoJson != null;
                        destWalkLeg.IsApproximate = false;
                        destWalkLeg.WalkingSource = "OSRM";
                        destWalkLeg.WalkingWarning = null;
                    }
                    
                    legs.Add(destWalkLeg);
                }
                break;
            }
            
            if (curr.UsedTransferEdge)
            {
                  var nextIdx = snapshot.StopIdToIndex[curr.NextStopId];
                  var transfer = snapshot.StopTransfers[curr.StopId].First(x => x.ToStopId == curr.NextStopId);
                  
                  var transferWalkLeg = new LegDto
                  {
                      Mode = "WALK",
                      DurationSeconds = transfer.WalkingTimeSeconds,
                      DistanceMeters = transfer.DistanceMeters,
                      FromStopId = curr.StopId,
                      FromStopName = snapshot.StopsByIndex[currIdx].StopName,
                      FromStopLat = snapshot.StopsByIndex[currIdx].StopLat,
                      FromStopLon = snapshot.StopsByIndex[currIdx].StopLon,
                      ToStopId = curr.NextStopId,
                      ToStopName = snapshot.StopsByIndex[nextIdx].StopName,
                      ToStopLat = snapshot.StopsByIndex[nextIdx].StopLat,
                      ToStopLon = snapshot.StopsByIndex[nextIdx].StopLon,
                      WalkingSource = "Haversine",
                      IsApproximate = true,
                      WalkingWarning = "Station-to-station static transfer",
                      HasGeometry = false
                  };
                  
                  var wrTransfer = await _walkingRoutingService.CalculateWalkingRouteAsync(
                      transferWalkLeg.FromStopLat.Value, transferWalkLeg.FromStopLon.Value,
                      transferWalkLeg.ToStopLat.Value, transferWalkLeg.ToStopLon.Value,
                      request.IncludeWalkingGeometry, "foot", cancellationToken);
                      
                  if (wrTransfer.State.IsSuccess)
                  {
                      transferWalkLeg.DistanceMeters = (int)wrTransfer.DistanceMeters;
                      transferWalkLeg.DurationSeconds = (int)wrTransfer.DurationSeconds;
                      transferWalkLeg.GeometryGeoJson = wrTransfer.GeometryGeoJson;
                      transferWalkLeg.HasGeometry = wrTransfer.GeometryGeoJson != null;
                      transferWalkLeg.IsApproximate = false;
                      transferWalkLeg.WalkingSource = "OSRM";
                      transferWalkLeg.WalkingWarning = null;
                  }
                  
                  legs.Add(transferWalkLeg);
                  currIdx = nextIdx;
                    // currRound stays the same for walk transfers
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
                    FromStopLat = snapshot.StopsByIndex[currIdx].StopLat,
                    FromStopLon = snapshot.StopsByIndex[currIdx].StopLon,
                    ToStopId = curr.AlightingStopId,
                    ToStopName = snapshot.StopsByIndex[alightIdx].StopName,
                    ToStopLat = snapshot.StopsByIndex[alightIdx].StopLat,
                    ToStopLon = snapshot.StopsByIndex[alightIdx].StopLon,
                    RawGtfsDepartureSeconds = boardTime.DepartureSeconds,
                    RawGtfsArrivalSeconds = alightTime.ArrivalSeconds,
                    DurationSeconds = alightTime.ArrivalSeconds - boardTime.DepartureSeconds
                });
                currIdx = alightIdx;
                    currRound--;
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
    private async Task<JourneyPlanSearchResponse> SearchArriveByJourneyV2AsyncInternal(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken, ulasim_veri_servisi.Models.Routing.V2TelemetryPayload telemetry)
    {
        var snapshot = _snapshotManager.GetActiveSnapshot();
        if (snapshot == null)
            throw new SnapshotUnavailableException("Routing graph is not loaded or is currently updating.");

        var trtOffset = TimeSpan.FromHours(3);
        var requestTimeTrt = request.DateTime!.Value.ToOffset(trtOffset);
        DateTime searchDate = requestTimeTrt.Date;
        
        if (searchDate < snapshot.FeedValidFrom.Date || searchDate > snapshot.FeedValidTo.Date)
        {
            return new JourneyPlanSearchResponse { ReasonCode = "FEED_STALE" };
        }

        int numStops = snapshot.StopsByIndex.Length;
        int maxRounds = Math.Min(3, request.MaxTransfers + 1);
        var labels = new BackwardRouteLabel[maxRounds + 1][];
        for (int r = 0; r <= maxRounds; r++)
        {
            labels[r] = new BackwardRouteLabel[numStops];
            for (int i = 0; i < numStops; i++)
            {
                labels[r][i] = new BackwardRouteLabel
                {
                    StopId = snapshot.StopsByIndex[i].StopId,
                    StopIndex = i,
                    AbsoluteDepartureSeconds = -1,
                    Round = -1,
                    TotalWalkDurationSeconds = int.MaxValue,
                    TotalWaitDurationSeconds = int.MaxValue
                };
            }
        }

        var originStops = FindNearbyStops(snapshot, request.Origin.Lat, request.Origin.Lon, request.MaxWalkingMeters);
        telemetry.OriginCandidateStopCount = originStops.Count;
        if (!originStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified origin walking radius.", true);
        
        var destinationStops = FindNearbyStops(snapshot, request.Destination.Lat, request.Destination.Lon, request.MaxWalkingMeters);
        telemetry.DestinationCandidateStopCount = destinationStops.Count;
        if (!destinationStops.Any()) throw new NoNearbyStopException("No valid transit stops found within the specified destination walking radius.", false);

        var origStopsSet = originStops.Select(s => s.StopId).ToHashSet();
        var origStopsWalkTime = originStops.ToDictionary(s => s.StopId, s => s.WalkingDurationSeconds);
        
        DateTime searchDateToday = requestTimeTrt.Date;
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
            return new JourneyPlanSearchResponse { ReasonCode = "NO_ACTIVE_SERVICE" };

        int prepBuffer = _configuration.GetValue<int>("JourneyPlan:BoardingPrepBufferSeconds", 60);
        int transferBuffer = _configuration.GetValue<int>("JourneyPlan:TransferSafetyBufferSeconds", 120);
        
        int targetArrivalTimeSeconds = (int)requestTimeTrt.TimeOfDay.TotalSeconds;
        int globalBestDepartureTime = -1; // We want to maximize this

        var activeStops = new HashSet<int>();

        // Initialize Destination Stops (Backwards)
        foreach (var ds in destinationStops)
        {
            if (snapshot.StopIdToIndex.TryGetValue(ds.StopId, out int idx))
            {
                // Must alight here by Target - Walk
                labels[0][idx].AbsoluteDepartureSeconds = targetArrivalTimeSeconds - ds.WalkingDurationSeconds;
                labels[0][idx].TotalWalkDurationSeconds = ds.WalkingDurationSeconds;
                labels[0][idx].TotalWaitDurationSeconds = 0;
                labels[0][idx].Round = -1;
                activeStops.Add(idx);
                
                if (origStopsWalkTime.TryGetValue(ds.StopId, out int finalWalk4))
                {
                    int originDepTime = labels[0][idx].AbsoluteDepartureSeconds - finalWalk4 - prepBuffer;
                    globalBestDepartureTime = Math.Max(globalBestDepartureTime, originDepTime);
                }
            }
        }

        for (int k = 1; k <= maxRounds; k++)
        {
            telemetry.RoundCount++;
            if (activeStops.Count == 0) break;

            var newlyActiveStops = new HashSet<int>();
            var activePatterns = new HashSet<string>();

            // Route Scan: find active patterns
            foreach (var stopIdx in activeStops)
            {
                // UPPER BOUND PRUNING (Maximization): Branch is sub-optimal
                if (labels[k - 1][stopIdx].AbsoluteDepartureSeconds <= globalBestDepartureTime)
                {
                    continue; 
                }

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
                telemetry.PatternScannedCount++;
                var stopsOnPattern = snapshot.PatternToStops[patternId];
                
                int? currentTripIndex = null;
                int? alightedStopIdx = null;
                int? alightedPatternIndex = null;
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
                        var boardTime = timetable[i];
                        
                        int departureTime = boardTime.DepartureSeconds + currentTripOffset;
                        int walkTime = labels[k - 1][alightedStopIdx.Value].TotalWalkDurationSeconds;
                        int waitTime = labels[k - 1][alightedStopIdx.Value].TotalWaitDurationSeconds; 
                        
                        // Wait time = Departure Time at board stop - Arrival time at alight stop? No.
                        // In ARRIVE_BY, user arrives at alight stop at T_alight. Trip arrives there at trip_arr. Wait time = T_alight - trip_arr.
                        var alightStopTime = timetable[alightedPatternIndex.Value];
                        int alightAbsoluteTime = alightStopTime.ArrivalSeconds + currentTripOffset;
                        int additionalWait = labels[k - 1][alightedStopIdx.Value].AbsoluteDepartureSeconds - alightAbsoluteTime;
                        waitTime += additionalWait;

                        // Prevent 0-minute or extremely short meaningless micro-legs (terminal crawls)
                        if (alightAbsoluteTime - departureTime <= 120)
                        {
                            // Skip boarding, it's not a useful transit leg
                        }
                        else
                        {
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
                                BoardingStopPatternIndex = i,
                                AlightingStopPatternIndex = alightedPatternIndex.Value,
                                UsedTransferEdge = false
                            };

                            if (DominatesBackward(newLabel, labels[k][stopIdx]))
                            {
                                telemetry.LabelUpdateCount++;
                                labels[k][stopIdx] = newLabel;
                                newlyActiveStops.Add(stopIdx);
                                
                                if (origStopsWalkTime.TryGetValue(stopId, out int finalWalk5))
                                {
                                    int origDepTime = departureTime - finalWalk5 - prepBuffer;
                                    globalBestDepartureTime = Math.Max(globalBestDepartureTime, origDepTime);
                                }
                            }
                        }
                    }

                    // 2. Is this stop active? Can we catch a LATER trip that arrives here <= labels[stopIdx].AbsoluteDepartureSeconds?
                    if (activeStops.Contains(stopIdx))
                    {
                        int targetAlightTime = labels[k - 1][stopIdx].AbsoluteDepartureSeconds;

                        int bestTripIndex = FindLatestTripIndex(snapshot, patternId, i, targetAlightTime, activeServicesToday, activeServicesYesterday, out int offsetSeconds);
                        telemetry.TripScannedCount++;
                        if (bestTripIndex != -1)
                        {
                            // If we weren't on a trip, or this new trip is a LATER trip (index is greater), we switch!
                            // Note: trips in PatternToTrips are sorted by departure time ascending. So a greater index means a later trip.
                            if (!currentTripIndex.HasValue || bestTripIndex > currentTripIndex.Value)
                            {
                                currentTripIndex = bestTripIndex;
                                alightedStopIdx = stopIdx;
                                alightedPatternIndex = i;
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
                        telemetry.TransferRelaxationCount++;
                        if (snapshot.StopIdToIndex.TryGetValue(tr.FromStopId, out int fromIdx))
                        {
                            // Must arrive at FromStop early enough to walk and transfer
                            int requiredArrivalAtFromStop = labels[k][stopIdx].AbsoluteDepartureSeconds - tr.WalkingTimeSeconds - transferBuffer;
                            var newLabel = new BackwardRouteLabel
                            {
                                StopId = tr.FromStopId,
                                StopIndex = fromIdx,
                                AbsoluteDepartureSeconds = requiredArrivalAtFromStop,
                                Round = k,
                                TotalWalkDurationSeconds = labels[k][stopIdx].TotalWalkDurationSeconds + tr.WalkingTimeSeconds,
                                TotalWaitDurationSeconds = labels[k][stopIdx].TotalWaitDurationSeconds,
                                NextStopId = sId,
                                UsedTransferEdge = true
                            };

                            if (DominatesBackward(newLabel, labels[k][fromIdx]))
                            {
                                telemetry.LabelUpdateCount++;
                                labels[k][fromIdx] = newLabel;
                                transferActiveStops.Add(fromIdx);
                                
                                if (origStopsWalkTime.TryGetValue(tr.FromStopId, out int finalWalk6))
                                {
                                    int origDepTime = requiredArrivalAtFromStop - finalWalk6 - prepBuffer;
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
        
        var validOrigStopsList = new List<dynamic>();
        foreach (var o in originStops)
        {
            if (!snapshot.StopIdToIndex.TryGetValue(o.StopId, out int idx)) continue;
            for (int r = 1; r <= maxRounds; r++)
            {
                if (labels[r][idx].AbsoluteDepartureSeconds != -1)
                {
                    validOrigStopsList.Add(new { Stop = o, Index = idx, Label = labels[r][idx], FinalRound = r });
                }
            }
        }
        var validOrigStops = validOrigStopsList
            .OrderByDescending(x => x.Label.AbsoluteDepartureSeconds - x.Stop.WalkingDurationSeconds)
            .Take(20)
            .ToList();

        foreach (var osData in validOrigStops)
        {
            var os = osData.Stop;
            int sIdx = osData.Index;
            var lbl = osData.Label;
            
            // Reconstruct forward!
            var itin = await ReconstructBackwardItinerary(snapshot, labels, osData.FinalRound, sIdx, os, destinationStops.FirstOrDefault(d => d.StopId == GetFinalDestinationStop(snapshot, labels, osData.FinalRound, sIdx)), request, targetArrivalTimeSeconds, prepBuffer, cancellationToken);
            if (itin != null) itineraries.Add(itin);
        }

        var simulatedItineraries = new List<ItineraryDto>();
        foreach (var itin in itineraries)
        {
            var firstTransit = itin.Legs.FirstOrDefault(l => l.Mode == "TRANSIT");
            if (firstTransit == null) continue;
            
            var originWalk = itin.Legs.First();
            int departureTimeSeconds = firstTransit.RawGtfsDepartureSeconds!.Value - originWalk.DurationSeconds - prepBuffer;
            
            bool isValid = CascadeSimulateItinerary(itin.Legs, searchDateToday, departureTimeSeconds, prepBuffer, transferBuffer, snapshot, activeServicesToday, activeServicesYesterday, out int finalArrivalSec, trtOffset);
            if (isValid)
            {
                DateTimeOffset midnightTrt = new DateTimeOffset(searchDateToday.Year, searchDateToday.Month, searchDateToday.Day, 0, 0, 0, trtOffset);
                itin.DepartureTime = midnightTrt.AddSeconds(departureTimeSeconds);
                itin.ArrivalTime = midnightTrt.AddSeconds(finalArrivalSec);
                
                // In ARRIVE_BY, we only keep it if the physical arrival is <= requested arrival
                if (itin.ArrivalTime <= request.DateTime!.Value)
                {
                    itin.PlanId = GeneratePlanId(itin, snapshot.FeedHash, "ARRIVE_BY");
                    simulatedItineraries.Add(itin);
                }
            }
        }

        // Sorting: Priority 1: Latest EffectiveDepartureTime (descending). 
        // We penalize long walks by effectively "departing earlier" for sorting purposes.
        var finalSorted = simulatedItineraries
            .GroupBy(x => string.Join("|", x.Legs.Where(l => l.Mode == "TRANSIT").Select(l => l.RouteId ?? "WALK")))
            .Select(g => g.OrderByDescending(x => x.DepartureTime.AddSeconds(-x.TotalWalkingTimeSeconds * 1.5))
                          .First())
            .OrderByDescending(i => i.DepartureTime.AddSeconds(-i.TotalWalkingTimeSeconds * 1.5))
            .ThenBy(i => i.TransferCount)
            .ThenBy(i => i.TotalWalkingTimeSeconds)
            .ThenBy(x => x.TotalWaitingTimeSeconds) // Priority 4: Least Wait
            .Take(request.MaxResults)
            .ToList();

        foreach (var iti in finalSorted)
        {
            iti.Fares = FareCalculatorService.CalculateFares(iti);
        }

        return new JourneyPlanSearchResponse { Itineraries = finalSorted, ReasonCode = JourneyPlanResolutionCode.SUCCESS.ToString() };
    }

    private string GetFinalDestinationStop(RoutingSnapshot snapshot, BackwardRouteLabel[][] labels, int finalRound, int startIdx)
    {
        int curr = startIdx;
        int currRound = finalRound;
        var visited = new HashSet<int>();
        while(currRound >= 0 && labels[currRound][curr].NextStopId != null)
        {
            if (!visited.Add(curr)) break; // Cycle detected
            if (snapshot.StopIdToIndex.TryGetValue(labels[currRound][curr].NextStopId, out int nextIdx))
            {
                if (!labels[currRound][curr].UsedTransferEdge) currRound--;
                curr = nextIdx;
            }
            else
            {
                break;
            }
        }
        return labels[currRound >= 0 ? currRound : 0][curr].StopId;
    }
}


