using ulasim_veri_servisi.Models.External;
using System.Diagnostics;
using TransportDataService;
using TransportDataService.Domain;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using ulasim_veri_servisi.Services;

using ulasim_veri_servisi.Exceptions;
using System.Net;


namespace ulasim_veri_servisi.Services
{
    public class ExternalEshotService : IExternalEshotService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(1);
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _context;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<ExternalEshotService> _logger;
        public ExternalEshotService(
       HttpClient httpClient,
       AppDbContext context,
       IMemoryCache memoryCache,
       ILogger<ExternalEshotService> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _memoryCache = memoryCache;
            _logger = logger;
        }
        
        public async Task<CachedResult<List<EshotBusDto>>> GetApproachingBusesAsync(string externalStopId, CancellationToken cancellationToken = default)


        {

            var url = $"https://openapi.izmir.bel.tr/api/iztek/duragayaklasanotobusler/{externalStopId}";

            var cacheKey = $"approaching-buses:{externalStopId}";


            if (_memoryCache.TryGetValue(cacheKey, out List<EshotBusDto>? cachedBuses))
            {
                return new CachedResult<List<EshotBusDto>>
                {
                    Data = cachedBuses!,
                    FromCache = true
                };
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    await LogExternalApiAsync(
                        "ApproachingBuses",
                        url,
                        (int)response.StatusCode,
                        (int)stopwatch.ElapsedMilliseconds,
                        false,
                        "Dış API'ye ulaşılamadı.");

                    throw new BadGatewayException("ESHOT servisinden veri alınamadı.");
                }

                var json = await response.Content.ReadAsStringAsync();

                var buses = JsonSerializer.Deserialize<List<EshotBusDto>>(json);

                if (buses == null)
                {
                    await LogExternalApiAsync(
                        "ApproachingBuses",
                        url,
                        (int)response.StatusCode,
                        (int)stopwatch.ElapsedMilliseconds,
                        false,
                        "Beklenmeyen response modeli.");

                    throw new BadGatewayException("ESHOT servisinden beklenen veri alınamadı.");
                }
                
                await LogExternalApiAsync(
                    "ApproachingBuses",
                    url,
                    (int)response.StatusCode,
                    (int)stopwatch.ElapsedMilliseconds,
                    true,
                    null);
                    
                var cacheOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration, Size = 1 };
                _memoryCache.Set(cacheKey, buses, cacheOptions);
                
                return new CachedResult<List<EshotBusDto>>
                {
                    Data = buses,
                    FromCache = false
                };
            }
            catch (JsonException ex)
            {
                await LogExternalApiAsync("ApproachingBuses", url, 500, (int)stopwatch.ElapsedMilliseconds, false, ex.Message);
                throw new BadGatewayException("ESHOT servisinden geçerli veri alınamadı.");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
            {
                await LogExternalApiAsync("ApproachingBuses", url, 408, (int)stopwatch.ElapsedMilliseconds, false, ex.Message);
                throw new ServiceUnavailableException("ESHOT servisine zaman aşımı nedeniyle ulaşılamadı.");
            }
            catch (HttpRequestException ex)
            {
                await LogExternalApiAsync("ApproachingBuses", url, 503, (int)stopwatch.ElapsedMilliseconds, false, ex.Message);
                throw new ServiceUnavailableException("ESHOT servisine ulaşılamıyor.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ApproachingBuses API çağrısında hata oluştu.");
                throw;
            }
        }
        public async Task<CachedResult<List<RouteVehicleDto>>> GetRouteVehiclesAsync(string routeNumber, CancellationToken cancellationToken = default)
        {
            var url = $"https://openapi.izmir.bel.tr/api/iztek/hatotobuskonumlari/{routeNumber}";

            var cacheKey = $"route-vehicles:{routeNumber}";

            if (_memoryCache.TryGetValue(cacheKey, out List<RouteVehicleDto>? cachedVehicles))
            {
                return new CachedResult<List<RouteVehicleDto>>
                {
                    Data = cachedVehicles!,
                    FromCache = true
                };
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    await LogExternalApiAsync(
                        "RouteVehicles",
                        url,
                        (int)response.StatusCode,
                        (int)stopwatch.ElapsedMilliseconds,
                        false,
                        "Dış API'ye ulaşılamadı.");

                    throw new BadGatewayException("ESHOT servisinden veri alınamadı.");
                }   
                
                var json = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<RouteVehiclesApiResponse>(json);

                if (apiResponse == null)
                {
                    await LogExternalApiAsync(
                        "RouteVehicles",
                        url,
                        (int)response.StatusCode,
                        (int)stopwatch.ElapsedMilliseconds,
                        false,
                        "Beklenmeyen response modeli.");

                    throw new BadGatewayException("ESHOT servisinden beklenen veri alınamadı.");
                }
                
                if (apiResponse.HataVarMi)
                {
                    var errorMsg = apiResponse.HataMesaj ?? "ESHOT API hata bildirdi.";
                    await LogExternalApiAsync(
                        "RouteVehicles",
                        url,
                        (int)response.StatusCode,
                        (int)stopwatch.ElapsedMilliseconds,
                        false,
                        errorMsg);

                    throw new BadGatewayException(errorMsg);
                }
                
                if (apiResponse.HatOtobusKonumlari == null)
                {
                    apiResponse.HatOtobusKonumlari = new List<RouteVehicleDto>();
                }

                var vehicles = apiResponse.HatOtobusKonumlari;
                await LogExternalApiAsync(
                       "RouteVehicles",
                       url,
                       (int)response.StatusCode,
                       (int)stopwatch.ElapsedMilliseconds,
                       true,
                       null);

                var cacheOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration, Size = 1 };
                _memoryCache.Set(cacheKey, vehicles, cacheOptions);
               
                return new CachedResult<List<RouteVehicleDto>>
                {
                    Data = vehicles,
                    FromCache = false
                };
            }
            catch (JsonException ex)
            {
                await LogExternalApiAsync("RouteVehicles", url, 500, (int)stopwatch.ElapsedMilliseconds, false, ex.Message);
                throw new BadGatewayException("ESHOT servisinden geçerli veri alınamadı.");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
            {
                await LogExternalApiAsync("RouteVehicles", url, 408, (int)stopwatch.ElapsedMilliseconds, false, ex.Message);
                throw new ServiceUnavailableException("ESHOT servisine zaman aşımı nedeniyle ulaşılamadı.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HttpRequestException occurred while fetching RouteVehicles.");
                await LogExternalApiAsync("RouteVehicles", url, 503, (int)stopwatch.ElapsedMilliseconds, false, ex.Message);
                throw new ServiceUnavailableException("ESHOT servisine ulaşılamıyor. Hata: " + ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RouteVehicles API çağrısında hata oluştu.");
                throw;
            }
        }

        private async Task LogExternalApiAsync(
            string endpointName,
            string requestUrl,
            int statusCode,
            int duration,
            bool isSuccessful,
            string? errorMessage)
        {
            _context.ExternalApiLogs.Add(new ExternalApiLog
            {
                EndpointName = endpointName,
                RequestUrl = requestUrl,
                HttpStatusCode = statusCode,
                ResponseDurationMs = duration,
                IsSuccessful = isSuccessful,
                ErrorMessage = errorMessage,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }
    }
}
