using TransportDataService;
using TransportDataService.Domain;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ulasım_veri_servisi.Services
{
    public class ExternalEshotService : IExternalEshotService
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _context;
        private readonly IMemoryCache _memoryCache;

        public ExternalEshotService(
      HttpClient httpClient,
      AppDbContext context,
      IMemoryCache memoryCache)
        {
            _httpClient = httpClient;
            _context = context;
            _memoryCache = memoryCache;
        }
        public async Task<CachedResult<List<EshotBusDto>>> GetApproachingBusesAsync(string externalStopId)


        {
            var url = $"https://openapi.izmir.bel.tr/api/iztek/duragayaklasanotobusler/{externalStopId}";

            var cacheKey = $"approaching-buses-{externalStopId}";

            if (_memoryCache.TryGetValue(cacheKey, out List<EshotBusDto>? cachedBuses))
            {
                return new CachedResult<List<EshotBusDto>>
                {
                    Data = cachedBuses!,
                    FromCache = true
                };
            }

            var start = DateTime.UtcNow;

            try
            {
                var startTime = DateTime.UtcNow;
                var response = await _httpClient.GetAsync(url);
                var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;



                if (!response.IsSuccessStatusCode)
                {
                    _context.ExternalApiLogs.Add(new ExternalApiLog
                    {
                        EndpointName = "ApproachingBuses",
                        RequestUrl = url,
                        HttpStatusCode = (int)response.StatusCode,
                        ResponseDurationMs = duration,
                        IsSuccessful = false,
                        ErrorMessage = "Dış API'ye ulaşılamadı.",
                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    throw new Exception("Dış API'ye ulaşılamadı.");
                }

                var json = await response.Content.ReadAsStringAsync();

                var buses = JsonSerializer.Deserialize<List<EshotBusDto>>(json)
                            ?? new List<EshotBusDto>();

                _context.ExternalApiLogs.Add(new ExternalApiLog
                {
                    EndpointName = "ApproachingBuses",
                    RequestUrl = url,
                    HttpStatusCode = (int)response.StatusCode,
                    ResponseDurationMs = duration,
                    IsSuccessful = true,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                _memoryCache.Set(
                    cacheKey,
                    buses,
                    TimeSpan.FromSeconds(20));
                _memoryCache.Set(
    cacheKey,
    buses,
    TimeSpan.FromSeconds(20));

                _context.ExternalApiLogs.Add(new ExternalApiLog
                {
                    EndpointName = "ApproachingBuses",
                    RequestUrl = url,
                    HttpStatusCode = (int)response.StatusCode,
                    ResponseDurationMs = duration,
                    IsSuccessful = response.IsSuccessStatusCode,
                    ErrorMessage = response.IsSuccessStatusCode ? null : "Dış API hatası",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return new CachedResult<List<EshotBusDto>>
                {
                    Data = buses,
                    FromCache = false
                };
            }
            catch (JsonException)
            {
                throw new Exception("JSON okunamadı.");
            }
        }
        public async Task<CachedResult<List<RouteVehicleDto>>> GetRouteVehiclesAsync(string routeNumber)
        {
            var url = $"https://openapi.izmir.bel.tr/api/iztek/hatotobuskonumlari/{routeNumber}";

            var cacheKey = $"route-vehicles-{routeNumber}";

            if (_memoryCache.TryGetValue(cacheKey, out List<RouteVehicleDto>? cachedVehicles))
            {
                return new CachedResult<List<RouteVehicleDto>>
                {
                    Data = cachedVehicles!,
                    FromCache = true
                };
            }

            var start = DateTime.UtcNow;

            try
            {

                var startTime = DateTime.UtcNow;

                var response = await _httpClient.GetAsync(url);

                var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;


                if (!response.IsSuccessStatusCode)
                {
                    _context.ExternalApiLogs.Add(new ExternalApiLog
                    {
                        EndpointName = "RouteVehicles",
                        RequestUrl = url,
                        HttpStatusCode = (int)response.StatusCode,
                        ResponseDurationMs = duration,
                        IsSuccessful = false,
                        ErrorMessage = "Dış API'ye ulaşılamadı.",
                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    throw new Exception("Dış API'ye ulaşılamadı.");
                }

                var json = await response.Content.ReadAsStringAsync();

                var apiResponse = JsonSerializer.Deserialize<RouteVehiclesApiResponse>(json);

                var vehicles = apiResponse?.HatOtobusKonumlari ?? new List<RouteVehicleDto>();

                _context.ExternalApiLogs.Add(new ExternalApiLog
                {
                    EndpointName = "RouteVehicles",
                    RequestUrl = url,
                    HttpStatusCode = (int)response.StatusCode,
                    ResponseDurationMs = duration,
                    IsSuccessful = true,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                _memoryCache.Set(
                    cacheKey,
                    vehicles,
                    TimeSpan.FromSeconds(20));
                _memoryCache.Set(
    cacheKey,
    vehicles,
    TimeSpan.FromSeconds(20));


                _context.ExternalApiLogs.Add(new ExternalApiLog
                {
                    EndpointName = "RouteVehicles",
                    RequestUrl = url,
                    HttpStatusCode = (int)response.StatusCode,
                    ResponseDurationMs = duration,
                    IsSuccessful = response.IsSuccessStatusCode,
                    ErrorMessage = response.IsSuccessStatusCode ? null : "Dış API hatası",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                return new CachedResult<List<RouteVehicleDto>>
                {
                    Data = vehicles,
                    FromCache = false
                };
            }
            catch (JsonException)
            {
                throw new Exception("JSON okunamadı.");
            }
        }


    }
}