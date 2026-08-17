using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ulasim_veri_servisi.Models;
using ulasim_veri_servisi.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services;

public class WalkingRoutingService
{
    private readonly IWalkingRouteProvider _provider;
    private readonly IMemoryCache _cache;
    private readonly WalkingRoutingCacheConfiguration _cacheConfig;
    private readonly ILogger<WalkingRoutingService> _logger;
    
    // In-flight requests for coalescing
    private readonly ConcurrentDictionary<string, Lazy<Task<WalkingResult>>> _inflightRequests = new();

    public WalkingRoutingService(
        IWalkingRouteProvider provider,
        IMemoryCache cache,
        IOptions<WalkingRoutingCacheConfiguration> cacheConfig,
        ILogger<WalkingRoutingService> logger)
    {
        _provider = provider;
        _cache = cache;
        _cacheConfig = cacheConfig.Value;
        _logger = logger;
    }

    public Task<WalkingResult> CalculateWalkingRouteAsync(double sourceLat, double sourceLon, double targetLat, double targetLon, bool includeGeometry = false, string profile = "foot", CancellationToken cancellationToken = default)
    {
        var srcLatStr = sourceLat.ToString("F5", CultureInfo.InvariantCulture);
        var srcLonStr = sourceLon.ToString("F5", CultureInfo.InvariantCulture);
        var tgtLatStr = targetLat.ToString("F5", CultureInfo.InvariantCulture);
        var tgtLonStr = targetLon.ToString("F5", CultureInfo.InvariantCulture);

        var providerName = _provider.GetType().Name;
        var cacheKey = $"route_{profile}_{srcLatStr}_{srcLonStr}_{tgtLatStr}_{tgtLonStr}_{includeGeometry}_{providerName}";

        if (_cache.TryGetValue(cacheKey, out WalkingResult? cachedResult) && cachedResult != null)
        {
            _logger.LogDebug("Cache HIT for route: {CacheKey}", cacheKey);
            return Task.FromResult(cachedResult);
        }

        // Request coalescing: try to add a new task, or get the existing in-flight task
        var lazyTask = _inflightRequests.GetOrAdd(cacheKey, k => new Lazy<Task<WalkingResult>>(() => FetchAndCacheRouteAsync(k, sourceLat, sourceLon, targetLat, targetLon, includeGeometry, profile, cancellationToken)));
        
        return lazyTask.Value;
    }

    private async Task<WalkingResult> FetchAndCacheRouteAsync(string cacheKey, double sourceLat, double sourceLon, double targetLat, double targetLon, bool includeGeometry, string profile, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Calculating {Profile} route from {SrcLat},{SrcLon} to {TgtLat},{TgtLon}", profile, sourceLat, sourceLon, targetLat, targetLon);
            var result = await _provider.GetWalkingRouteAsync(sourceLat, sourceLon, targetLat, targetLon, includeGeometry, profile, cancellationToken);

            if (result.State.IsSuccess)
            {
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(_cacheConfig.TtlMinutes))
                    .SetSize(1); // Ensure it conforms to IMemoryCache size limits if configured

                _cache.Set(cacheKey, result, cacheEntryOptions);
            }
            else
            {
                _logger.LogWarning("Not caching failed walking route: {CacheKey}", cacheKey);
            }

            return result;
        }
        finally
        {
            // Remove the task from in-flight dictionary once it's done (success or failure)
            _inflightRequests.TryRemove(cacheKey, out _);
        }
    }
}
