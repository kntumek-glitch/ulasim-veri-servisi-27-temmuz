using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ulasim_veri_servisi.HealthChecks;

public class EshotApiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;

    public EshotApiHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Using the base URL for the OpenAPI for a simple reachability check
            var response = await client.GetAsync("https://openapi.izmir.bel.tr/", cancellationToken);

            // If the server responds (even if it's 404 Not Found or 401 Unauthorized for this root path), 
            // it means the host is up and reachable.
            if (response.IsSuccessStatusCode || 
                response.StatusCode == System.Net.HttpStatusCode.NotFound || 
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return HealthCheckResult.Healthy("ESHOT dış servisi erişilebilir durumda.");
            }

            return HealthCheckResult.Degraded($"ESHOT servisi beklenmeyen bir durum kodu döndürdü: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            // As requested, the failure of external service causes the app to be 'Degraded' rather than entirely 'Unhealthy'
            return HealthCheckResult.Degraded("ESHOT servisine ulaşılamıyor. Uygulama lokal / önbellek verileriyle çalışmaya devam edebilir.", ex);
        }
    }
}

