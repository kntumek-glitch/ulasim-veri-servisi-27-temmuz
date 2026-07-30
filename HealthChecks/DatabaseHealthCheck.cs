using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportDataService;

namespace ulasim_veri_servisi.HealthChecks;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1. Check Connection
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("PostgreSQL veritabanına bağlanılamıyor.");
            }

            // 2. Check Migrations
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                return HealthCheckResult.Unhealthy($"Veritabanında uygulanmamış {pendingMigrations.Count()} migration bulunuyor.");
            }

            return HealthCheckResult.Healthy("Veritabanı bağlantısı başarılı ve tüm migrationlar uygulanmış durumda.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Veritabanı sağlık kontrolü sırasında hata oluştu.", ex);
        }
    }
}

