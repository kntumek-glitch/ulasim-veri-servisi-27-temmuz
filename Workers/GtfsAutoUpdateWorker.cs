using TransportDataService;
using ulasım_veri_servisi.Exceptions;
using ulasım_veri_servisi.Services;

namespace ulasım_veri_servisi.Workers;

public class GtfsAutoUpdateWorker : BackgroundService
{
    public static DateTime? NextRunTime { get; private set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GtfsAutoUpdateWorker> _logger;

    public GtfsAutoUpdateWorker(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<GtfsAutoUpdateWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Okunacak ortam değişkeni: Gtfs:AutoUpdateIntervalMinutes
        // Varsayılan: 720 dakika (12 saat)
        var intervalMinutes = _configuration.GetValue<int>("Gtfs:AutoUpdateIntervalMinutes", 720);
        
        _logger.LogInformation("GtfsAutoUpdateWorker is starting. Interval: {intervalMinutes} minutes.", intervalMinutes);

        // Uygulama ilk kalktığında diğer servislerin hazır olması için ufak bir bekleme (opsiyonel)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("GtfsAutoUpdateWorker running at: {time}", DateTimeOffset.Now);

                using var scope = _serviceProvider.CreateScope();
                var importService = scope.ServiceProvider.GetRequiredService<IGtfsImportService>();

                var result = await importService.ImportAsync(stoppingToken);
                _logger.LogInformation("GtfsAutoUpdateWorker finished import with status: {status}", result.Status);
            }
            catch (ConcurrentImportException ex)
            {
                _logger.LogWarning("GtfsAutoUpdateWorker skipped import because another import is already running: {message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during background GTFS import.");
            }

            try
            {
                // Bir sonraki periyoda kadar bekle
                NextRunTime = DateTime.UtcNow.AddMinutes(intervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                NextRunTime = null;
                _logger.LogInformation("GtfsAutoUpdateWorker is stopping.");
                break;
            }
        }
    }
}
