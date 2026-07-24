using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TransportDataService;

namespace ulasım_veri_servisi.Filters
{
    public class GtfsETagCacheFilterAttribute : IAsyncActionFilter
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<GtfsETagCacheFilterAttribute> _logger;

        public GtfsETagCacheFilterAttribute(AppDbContext context, IMemoryCache cache, ILogger<GtfsETagCacheFilterAttribute> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.HttpContext.Request.Method != HttpMethods.Get)
            {
                await next();
                return;
            }

            var activeRun = await _context.GtfsImportRuns
                .Where(r => r.IsActive)
                .Select(r => new { r.FileHash })
                .FirstOrDefaultAsync();

            string currentHash = activeRun?.FileHash ?? "no-hash";
            string eTag = $"\"{currentHash}\"";
            
            context.HttpContext.Response.Headers.ETag = eTag;

            // 1. ETag Check
            if (context.HttpContext.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch))
            {
                if (ifNoneMatch == eTag || ifNoneMatch == "*")
                {
                    context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
                    return;
                }
            }

            // 2. Cache Check
            var requestPath = context.HttpContext.Request.Path;
            var requestQueryString = context.HttpContext.Request.QueryString;
            string cacheKey = $"gtfs:{currentHash}:{requestPath}{requestQueryString}";

            if (_cache.TryGetValue(cacheKey, out IActionResult? cachedResult) && cachedResult != null)
            {
                context.Result = cachedResult;
                return;
            }

            // Execute the action
            var executedContext = await next();

            // Cache the result if successful
            if (executedContext.Result is ObjectResult objectResult && 
                (objectResult.StatusCode == StatusCodes.Status200OK || objectResult.StatusCode == null))
            {
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                };
                
                _cache.Set(cacheKey, executedContext.Result, cacheOptions);
            }
        }
    }
}
