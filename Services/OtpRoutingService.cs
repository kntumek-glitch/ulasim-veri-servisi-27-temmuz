using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransportDataService.Models.Gtfs.JourneyPlan;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services;

public class OtpRoutingService : IJourneyPlanningService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OtpRoutingService> _logger;
    private readonly string _otpBaseUrl;

    public OtpRoutingService(HttpClient httpClient, IConfiguration configuration, ILogger<OtpRoutingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _otpBaseUrl = configuration["Otp:BaseUrl"] ?? "http://localhost:8081";
    }

    public async Task<JourneyPlanSearchResponse> SearchJourneyAsync(JourneyPlanSearchRequest request, CancellationToken cancellationToken = default)
    {
        // Convert to Turkey local time (UTC+3) to send to OTP, which expects local time
        var departure = request.DepartureDateTime ?? DateTimeOffset.Now;
        var turkeyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        var localDeparture = TimeZoneInfo.ConvertTime(departure, turkeyTimeZone);
        
        var date = localDeparture.ToString("yyyy-MM-dd");
        var time = localDeparture.ToString("HH:mm");

        // Modes: TRANSIT, WALK
        string url = $"{_otpBaseUrl}/otp/routers/default/plan?fromPlace={request.Origin.Lat},{request.Origin.Lon}&toPlace={request.Destination.Lat},{request.Destination.Lon}&date={date}&time={time}&mode=TRANSIT,WALK&maxTransfers={request.MaxTransfers}&maxWalkDistance={request.MaxWalkingMeters}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("OTP API returned error: {StatusCode} {Content}", response.StatusCode, errorContent);
                return new JourneyPlanSearchResponse { ReasonCode = JourneyPlanResolutionCode.INTERNAL_ERROR.ToString() };
            }

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            var otpDoc = JsonDocument.Parse(jsonString);

            var errorElement = otpDoc.RootElement.GetProperty("error");
            if (errorElement.ValueKind != JsonValueKind.Null && errorElement.ValueKind != JsonValueKind.Undefined)
            {
                var msg = errorElement.GetProperty("message").GetString();
                _logger.LogWarning("OTP returned a plan error: {Msg}", msg);
                return new JourneyPlanSearchResponse { ReasonCode = JourneyPlanResolutionCode.NO_ROUTE_FOUND.ToString() };
            }

            var plan = otpDoc.RootElement.GetProperty("plan");
            var itineraries = plan.GetProperty("itineraries");

            var result = new JourneyPlanSearchResponse
            {
                ReasonCode = JourneyPlanResolutionCode.SUCCESS.ToString(),
                Metadata = new JourneyPlanMetadataDto
                {
                    AlgorithmVersion = "OTP-2",
                    DataSource = "OTP",
                    SearchMode = "TRANSIT"
                }
            };

            int index = 0;
            foreach (var itin in itineraries.EnumerateArray())
            {
                if (index >= request.MaxResults) break;

                var startTime = DateTimeOffset.FromUnixTimeMilliseconds(itin.GetProperty("startTime").GetInt64());
                var endTime = DateTimeOffset.FromUnixTimeMilliseconds(itin.GetProperty("endTime").GetInt64());
                
                var itineraryDto = new ItineraryDto
                {
                    PlanId = Guid.NewGuid().ToString(),
                    DataSource = "OTP",
                    DepartureTime = startTime,
                    ArrivalTime = endTime,
                    TransferCount = itin.GetProperty("transfers").GetInt32(),
                    TotalWalkingDistanceMeters = (int)itin.GetProperty("walkDistance").GetDouble(),
                    TotalWalkingTimeSeconds = itin.GetProperty("walkTime").GetInt32(),
                    TotalWaitingTimeSeconds = itin.GetProperty("waitingTime").GetInt32(),
                    TotalInVehicleTimeSeconds = itin.GetProperty("transitTime").GetInt32()
                };

                var legs = itin.GetProperty("legs");
                foreach (var leg in legs.EnumerateArray())
                {
                    var mode = leg.GetProperty("mode").GetString(); // WALK, BUS, TRAM, SUBWAY, etc.
                    bool isTransit = mode != "WALK" && mode != "BICYCLE" && mode != "CAR";
                    
                    var legDto = new LegDto
                    {
                        Mode = isTransit ? "TRANSIT" : "WALK",
                        DistanceMeters = (int)leg.GetProperty("distance").GetDouble(),
                        DurationSeconds = leg.GetProperty("duration").GetInt32(),
                        DurationMinutes = leg.GetProperty("duration").GetInt32() / 60,
                        DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds(leg.GetProperty("startTime").GetInt64()),
                        ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds(leg.GetProperty("endTime").GetInt64()),
                    };

                    var from = leg.GetProperty("from");
                    var to = leg.GetProperty("to");

                    legDto.FromStopLat = from.GetProperty("lat").GetDouble();
                    legDto.FromStopLon = from.GetProperty("lon").GetDouble();
                    legDto.FromStopName = from.GetProperty("name").GetString();
                    if (from.TryGetProperty("stopId", out var fromStopId)) legDto.FromStopId = fromStopId.GetString()?.Split(':').LastOrDefault();

                    legDto.ToStopLat = to.GetProperty("lat").GetDouble();
                    legDto.ToStopLon = to.GetProperty("lon").GetDouble();
                    legDto.ToStopName = to.GetProperty("name").GetString();
                    if (to.TryGetProperty("stopId", out var toStopId)) legDto.ToStopId = toStopId.GetString()?.Split(':').LastOrDefault();

                    if (isTransit)
                    {
                        legDto.RouteShortName = leg.GetProperty("routeShortName").GetString();
                        if (leg.TryGetProperty("routeId", out var rId)) legDto.RouteId = rId.GetString()?.Split(':').LastOrDefault();
                        if (leg.TryGetProperty("tripId", out var tId)) legDto.TripId = tId.GetString()?.Split(':').LastOrDefault();
                        if (leg.TryGetProperty("headsign", out var headsign)) legDto.Headsign = headsign.GetString();
                    }

                    // Decode Geometry GeoJSON
                    if (leg.TryGetProperty("legGeometry", out var legGeom) && legGeom.TryGetProperty("points", out var points))
                    {
                        var polyline = points.GetString();
                        if (!string.IsNullOrEmpty(polyline))
                        {
                            legDto.GeometryGeoJson = new {
                                type = "LineString",
                                coordinates = DecodePolyline(polyline)
                            };
                            legDto.HasGeometry = true;
                        }
                    }

                    // Intermediate Stops
                    if (request.IncludeIntermediateStops && leg.TryGetProperty("intermediateStops", out var intermediateStops) && intermediateStops.ValueKind == JsonValueKind.Array)
                    {
                        legDto.IntermediateStops = new List<IntermediateStopDto>();
                        int stopSeq = 1;
                        foreach (var istop in intermediateStops.EnumerateArray())
                        {
                            var istopDto = new IntermediateStopDto
                            {
                                StopName = istop.GetProperty("name").GetString() ?? "",
                                Lat = istop.GetProperty("lat").GetDouble(),
                                Lon = istop.GetProperty("lon").GetDouble(),
                                StopSequence = stopSeq++
                            };
                            
                            if (istop.TryGetProperty("stopId", out var sId)) istopDto.StopId = sId.GetString()?.Split(':').LastOrDefault() ?? "";
                            if (istop.TryGetProperty("arrival", out var arrTime)) istopDto.ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds(arrTime.GetInt64());
                            
                            legDto.IntermediateStops.Add(istopDto);
                        }
                        legDto.IntermediateStopCount = legDto.IntermediateStops.Count;
                    }

                    itineraryDto.Legs.Add(legDto);
                }

                // Adjust the itinerary departure time to exclude the initial wait time (OTP quirk)
                var firstTransitLeg = itineraryDto.Legs.FirstOrDefault(l => l.Mode == "TRANSIT");
                if (firstTransitLeg != null)
                {
                    var walkLegsBeforeTransit = itineraryDto.Legs.TakeWhile(l => l.Mode == "WALK").ToList();
                    if (walkLegsBeforeTransit.Any())
                    {
                        int initialWalkDurationSecs = walkLegsBeforeTransit.Sum(l => l.DurationSeconds);
                        var optimalDepartureTime = firstTransitLeg.DepartureTime.Value.AddSeconds(-initialWalkDurationSecs);
                        int initialWaitSecs = (int)(optimalDepartureTime - itineraryDto.DepartureTime).TotalSeconds;
                        
                        if (initialWaitSecs > 0)
                        {
                            itineraryDto.InitialWaitTimeSeconds = initialWaitSecs;
                            itineraryDto.TotalWaitingTimeSeconds -= initialWaitSecs;
                            itineraryDto.DepartureTime = optimalDepartureTime;
                            
                            var currentStartTime = optimalDepartureTime;
                            foreach (var walkLeg in walkLegsBeforeTransit)
                            {
                                walkLeg.DepartureTime = currentStartTime;
                                currentStartTime = currentStartTime.AddSeconds(walkLeg.DurationSeconds);
                                walkLeg.ArrivalTime = currentStartTime;
                            }
                        }
                    }
                    else
                    {
                        int initialWaitSecs = (int)(firstTransitLeg.DepartureTime.Value - itineraryDto.DepartureTime).TotalSeconds;
                        if (initialWaitSecs > 0)
                        {
                            itineraryDto.InitialWaitTimeSeconds = initialWaitSecs;
                            itineraryDto.TotalWaitingTimeSeconds -= initialWaitSecs;
                            itineraryDto.DepartureTime = firstTransitLeg.DepartureTime.Value;
                        }
                    }
                }

                result.Itineraries.Add(itineraryDto);
                index++;
            }

            if (!result.Itineraries.Any())
            {
                result.ReasonCode = JourneyPlanResolutionCode.NO_ROUTE_FOUND.ToString();
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while calling OTP API");
            return new JourneyPlanSearchResponse { ReasonCode = JourneyPlanResolutionCode.INTERNAL_ERROR.ToString() };
        }
    }

    public async Task<JourneyPlanSearchResponse> SearchJourneyV2Async(JourneyPlanV2SearchRequest request, CancellationToken cancellationToken = default)
    {
        // Proxy to V1 mapping
        var req = new JourneyPlanSearchRequest
        {
            Origin = request.Origin,
            Destination = request.Destination,
            DepartureDateTime = request.DateTime,
            MaxTransfers = request.MaxTransfers,
            MaxWalkingMeters = request.MaxWalkingMeters,
            MaxResults = request.MaxResults,
            IncludeIntermediateStops = request.IncludeIntermediateStops
        };
        return await SearchJourneyAsync(req, cancellationToken);
    }

    // Standard Google Polyline Decoder
    private static List<double[]> DecodePolyline(string encoded)
    {
        var poly = new List<double[]>();
        int index = 0, len = encoded.Length;
        int lat = 0, lng = 0;

        while (index < len)
        {
            int b, shift = 0, result = 0;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
            lat += dlat;

            shift = 0;
            result = 0;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
            lng += dlng;

            poly.Add(new double[] { (double)lng / 1E5, (double)lat / 1E5 });
        }
        return poly;
    }
}
