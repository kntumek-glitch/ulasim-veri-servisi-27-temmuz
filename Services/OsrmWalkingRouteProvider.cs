using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ulasim_veri_servisi.Models;
using ulasim_veri_servisi.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services;

public class OsrmWalkingRouteProvider : IWalkingRouteProvider
{
    private readonly HttpClient _httpClient;
    private readonly OsrmConfiguration _config;
    private readonly ILogger<OsrmWalkingRouteProvider> _logger;

    public OsrmWalkingRouteProvider(HttpClient httpClient, IOptions<OsrmConfiguration> options, ILogger<OsrmWalkingRouteProvider> logger)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "UlasimVeriServisi/1.0");
        _config = options.Value;
        _logger = logger;
    }

    public async Task<WalkingResult> GetWalkingRouteAsync(double sourceLat, double sourceLon, double targetLat, double targetLon, bool includeGeometry = false, string profile = "foot", CancellationToken cancellationToken = default)
    {
        try
        {
            var actualProfile = string.IsNullOrWhiteSpace(profile) ? _config.Profile : profile;
            
            var baseUrl = actualProfile == "car" ? _config.CarBaseUrl : _config.BaseUrl;
            
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogError("OSRM BaseUrl is not configured for profile {Profile}.", actualProfile);
                return new WalkingResult { State = ErrorState.Failure("OSRM BaseUrl is missing", "CONFIG_ERROR") };
            }

            var srcLatStr = sourceLat.ToString(CultureInfo.InvariantCulture);
            var srcLonStr = sourceLon.ToString(CultureInfo.InvariantCulture);
            var tgtLatStr = targetLat.ToString(CultureInfo.InvariantCulture);
            var tgtLonStr = targetLon.ToString(CultureInfo.InvariantCulture);


            // OSRM format: /route/v1/{profile}/{coordinates}?overview=full
            // Coordinates format: {longitude},{latitude};{longitude},{latitude}
            var geometries = includeGeometry ? "geojson" : "polyline";
            var path = $"{baseUrl}/route/v1/{actualProfile}/{srcLonStr},{srcLatStr};{tgtLonStr},{tgtLatStr}?overview=full&geometries={geometries}&alternatives=3";
            
            var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM API returned status code {StatusCode}", (int)response.StatusCode);
                return new WalkingResult { State = ErrorState.Failure("Yönlendirme sunucusuna erişilemedi.", "API_ERROR") };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var codeElement))
            {
                var code = codeElement.GetString();
                if (code == "Ok")
                {
                    if (root.TryGetProperty("waypoints", out var waypointsElement))
                    {
                        foreach (var waypoint in waypointsElement.EnumerateArray())
                        {
                            if (waypoint.TryGetProperty("distance", out var distElement))
                            {
                                if (distElement.GetDouble() > 2000) // 2000m snap tolerance (relaxed for car/far points)
                                {
                                    _logger.LogWarning("OSRM waypoint distance {Distance} exceeds 2000m threshold", distElement.GetDouble());
                                    return new WalkingResult { State = ErrorState.Failure("Verilen koordinatlar yol ağına çok uzak.", "UNROUTABLE_LOCATION") };
                                }
                            }
                        }
                    }

                    if (root.TryGetProperty("routes", out var routesElement) && routesElement.GetArrayLength() > 0)
                    {
                        var route = routesElement[0];
                        var distance = route.GetProperty("distance").GetDouble();
                        var duration = route.GetProperty("duration").GetDouble();
                        
                        double haversineDist = GetHaversineDistance(sourceLat, sourceLon, targetLat, targetLon);
                        
                        double originalDuration = duration;
                        if (actualProfile == "foot")
                        {
                            // Enforce a realistic walking speed (1.4 m/s) over the valid distance
                            duration = distance / 1.4;
                        }
                        
                        Console.WriteLine($"[OSRM Walking] Profile: {profile}, ActualProfile: {actualProfile}, Distance: {distance}, OriginalDuration: {originalDuration}, NewDuration: {duration}");
                        
                        var geometryProp = route.GetProperty("geometry");

                        var result = new WalkingResult
                        {
                            State = ErrorState.Success(),
                            DistanceMeters = distance,
                            DurationSeconds = duration,
                            Alternatives = new System.Collections.Generic.List<WalkingRouteAlternative>()
                        };

                        if (includeGeometry)
                        {
                            var rawJson = geometryProp.GetRawText();
                            result.GeometryGeoJson = JsonSerializer.Deserialize<object>(rawJson);
                        }
                        else
                        {
                            result.EncodedPolyline = geometryProp.GetString();
                        }

                        // Parse alternatives
                        for (int i = 1; i < routesElement.GetArrayLength(); i++)
                        {
                            var altRoute = routesElement[i];
                            var altDistance = altRoute.GetProperty("distance").GetDouble();
                            var altDuration = altRoute.GetProperty("duration").GetDouble();
                            
                            if (actualProfile == "foot")
                            {
                                altDuration = altDistance / 1.2;
                            }

                            var altGeometryProp = altRoute.GetProperty("geometry");

                            var altObj = new WalkingRouteAlternative
                            {
                                DistanceMeters = altDistance,
                                DurationSeconds = altDuration
                            };

                            if (includeGeometry)
                            {
                                altObj.GeometryGeoJson = JsonSerializer.Deserialize<object>(altGeometryProp.GetRawText());
                            }
                            else
                            {
                                altObj.EncodedPolyline = altGeometryProp.GetString();
                            }

                            result.Alternatives.Add(altObj);
                        }

                        return result;
                    }
                }
                else
                {
                    _logger.LogWarning("OSRM returned non-Ok code: {Code} for request {Path}", code, path);
                    return new WalkingResult { State = ErrorState.Failure($"OSRM returned code: {code}", "NO_ROUTE") };
                }
            }

            return new WalkingResult { State = ErrorState.Failure("Bilinmeyen yanıt formatı.", "INVALID_FORMAT") };
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "OSRM API request was cancelled by the client.");
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "OSRM API request timed out.");
            return new WalkingResult { State = ErrorState.Failure("Yönlendirme isteği zaman aşımına uğradı.", "TIMEOUT") };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "OSRM API returned malformed JSON.");
            return new WalkingResult { State = ErrorState.Failure("Yönlendirme sunucusundan geçersiz veri alındı.", "MALFORMED_JSON") };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to OSRM API.");
            return new WalkingResult { State = ErrorState.Failure("Beklenmeyen bir ağ hatası oluştu.", "NETWORK_ERROR") };
        }
    }

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
        return 6371000 * c;
    }
}
