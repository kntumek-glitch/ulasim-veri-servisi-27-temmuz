using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ulasım_veri_servisi.Filters
{
    public class AdminKeyAuthAttribute : IAsyncActionFilter
    {
        private const string ApiKeyHeaderName = "X-Admin-Key";
        private readonly IConfiguration _configuration;

        public AdminKeyAuthAttribute(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
            {
                context.Result = new UnauthorizedObjectResult(new { Message = "Invalid or missing admin key" });
                return;
            }

            var apiKey = _configuration.GetValue<string>("AdminSettings:ApiKey");

            if (string.IsNullOrEmpty(apiKey))
            {
                // In production, if key is not configured, deny all requests for safety.
                context.Result = new ObjectResult(new { Message = "Server configuration error: Admin key is not configured." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
                return;
            }

            if (!apiKey.Equals(extractedApiKey))
            {
                context.Result = new ObjectResult(new { Message = "Invalid or missing admin key" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }

            await next();
        }
    }
}
