using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ulasim_veri_servisi.Services
{
    public class ReverseGeocodeService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ReverseGeocodeService> _logger;

        public ReverseGeocodeService(HttpClient httpClient, IMemoryCache cache, ILogger<ReverseGeocodeService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
        }

        public async Task<string> GetLocationContextAsync(double? latitude, double? longitude, CancellationToken cancellationToken = default)
        {
            if (latitude == null || longitude == null) return "Bilinmeyen Konum";

            // Round coordinates to ~100m grid for caching (approx 3 decimal places)
            double latRounded = Math.Round(latitude.Value, 3);
            double lonRounded = Math.Round(longitude.Value, 3);
            string cacheKey = $"ReverseGeocode_{latRounded}_{lonRounded}";

            if (_cache.TryGetValue(cacheKey, out string? cachedContext))
            {
                return cachedContext ?? "Bilinmeyen Konum";
            }

            try
            {
                // api.bigdatacloud.net is a free, client-friendly reverse geocoding API requiring no auth
                string url = $"https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={latRounded.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={lonRounded.ToString(System.Globalization.CultureInfo.InvariantCulture)}&localityLanguage=tr";
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    
                    string locality = root.TryGetProperty("locality", out var locElement) ? locElement.GetString() ?? "" : "";
                    string city = root.TryGetProperty("city", out var cityElement) ? cityElement.GetString() ?? "" : "";
                    
                    string context = "";
                    if (!string.IsNullOrEmpty(locality) && !string.IsNullOrEmpty(city))
                    {
                        context = $"{locality}, {city}";
                    }
                    else if (!string.IsNullOrEmpty(locality))
                    {
                        context = locality;
                    }
                    else if (!string.IsNullOrEmpty(city))
                    {
                        context = city;
                    }
                    else
                    {
                        context = "İzmir";
                    }

                    // Cache for 24 hours since geographical grids don't change names often
                    var cacheOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24), Size = 1 };
                    _cache.Set(cacheKey, context, cacheOptions);
                    return context;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reverse geocode {Lat},{Lon}", latRounded, lonRounded);
            }

            return "İzmir"; // Fallback
        }
    }
}
