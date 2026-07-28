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
            
            var requestPath = context.HttpContext.Request.Path;
            var requestQueryString = context.HttpContext.Request.QueryString;
            var acceptHeader = context.HttpContext.Request.Headers.Accept.ToString();
            
            string rawETagInput = $"{currentHash}|{requestPath}|{requestQueryString}|{acceptHeader}";
            
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawETagInput));
            string eTag = $"\"{Convert.ToBase64String(bytes)}\"";
            
            string cacheKey = $"gtfs:{currentHash}:{requestPath}{requestQueryString}";
            
            // Cache Check First
            if (_cache.TryGetValue(cacheKey, out object? cachedData) && cachedData != null)
            {
                // Verify ETag
                if (context.HttpContext.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) && 
                    (ifNoneMatch == eTag || ifNoneMatch == "*"))
                {
                    context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
                    return;
                }

                context.HttpContext.Response.Headers.ETag = eTag;
                context.Result = new OkObjectResult(cachedData);
                return;
            }

            // Execute the action if not in cache (so we can determine if it's 404 or 200)
            var executedContext = await next();

            // Cache the result if successful and has data
            if (executedContext.Result is ObjectResult objectResult && 
                (objectResult.StatusCode == StatusCodes.Status200OK || objectResult.StatusCode == null) &&
                objectResult.Value != null)
            {
                context.HttpContext.Response.Headers.ETag = eTag;
                
                // If client somehow sent the correct ETag (e.g. from an expired cache), return 304
                if (context.HttpContext.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatchAction) && 
                    (ifNoneMatchAction == eTag || ifNoneMatchAction == "*"))
                {
                    executedContext.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
                }

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                    Size = 1
                };
                
                _cache.Set(cacheKey, objectResult.Value, cacheOptions);
            }
        }
    }
}
