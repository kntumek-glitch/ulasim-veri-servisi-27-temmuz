using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransportDataService;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services;

public class GtfsTransferCalculationService : IGtfsTransferCalculationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GtfsTransferCalculationService> _logger;
    private readonly IConfiguration _configuration;

    public GtfsTransferCalculationService(
        IServiceScopeFactory scopeFactory,
        ILogger<GtfsTransferCalculationService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task CalculateTransfersAsync(int gtfsImportRunId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting GtfsTransfer calculation for RunId: {RunId}", gtfsImportRunId);
        
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var transferList = (await ComputeTransfersAsync(gtfsImportRunId, context, cancellationToken))
            .GroupBy(t => new { t.GtfsImportRunId, t.FromStopId, t.ToStopId })
            .Select(g => g.First())
            .ToList();
        _logger.LogInformation("Calculated {TotalTransfers} transfer edges. Starting bulk insert...", transferList.Count);

        // Bulk insert in batches of 10_000
        int batchSize = 10000;
        for (int i = 0; i < transferList.Count; i += batchSize)
        {
            var batch = transferList.Skip(i).Take(batchSize).ToList();
            
            using var batchScope = _scopeFactory.CreateScope();
            var batchContext = batchScope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            await batchContext.GtfsTransfers.AddRangeAsync(batch, cancellationToken);
            await batchContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Inserted {Inserted}/{TotalTransfers} transfer records.", Math.Min(i + batchSize, transferList.Count), transferList.Count);
        }

        _logger.LogInformation("GtfsTransfer calculation completed successfully for RunId: {RunId}", gtfsImportRunId);
    }

    public async Task<int> RebuildTransfersAsync(int gtfsImportRunId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting GtfsTransfer REBUILD for RunId: {RunId}", gtfsImportRunId);
        
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var transferList = await ComputeTransfersAsync(gtfsImportRunId, context, cancellationToken);
        _logger.LogInformation("Calculated {TotalTransfers} transfer edges. Starting transactional replace...", transferList.Count);

        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Eksi verileri temizle
            await context.GtfsTransfers.Where(t => t.GtfsImportRunId == gtfsImportRunId).ExecuteDeleteAsync(cancellationToken);
            
            // Yeni verileri ekle
            int batchSize = 10000;
            for (int i = 0; i < transferList.Count; i += batchSize)
            {
                var batch = transferList.Skip(i).Take(batchSize).ToList();
                await context.GtfsTransfers.AddRangeAsync(batch, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Inserted {Inserted}/{TotalTransfers} transfer records in rebuild.", Math.Min(i + batchSize, transferList.Count), transferList.Count);
            }
            
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("GtfsTransfer REBUILD completed successfully for RunId: {RunId}", gtfsImportRunId);
            return transferList.Count;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "GtfsTransfer REBUILD failed for RunId: {RunId}. Transaction rolled back.", gtfsImportRunId);
            throw;
        }
    }

    private async Task<List<GtfsTransfer>> ComputeTransfersAsync(int gtfsImportRunId, AppDbContext context, CancellationToken cancellationToken)
    {
        int maxWalkMeters = _configuration.GetValue<int>("JourneyPlan:MaxTransferWalkMeters", 1500);
        double walkingSpeed = _configuration.GetValue<double>("JourneyPlan:WalkingSpeedMetersPerSecond", 1.2);

        // Feed henüz aktif (IsActive = true) olmadığı için IgnoreQueryFilters kullanıyoruz!
        var stops = await context.GtfsStops
            .IgnoreQueryFilters()
            .Where(s => s.GtfsImportRunId == gtfsImportRunId && s.StopLat != 0 && s.StopLon != 0)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} stops for RunId {RunId}. Building spatial grid...", stops.Count, gtfsImportRunId);

        var grid = new Dictionary<string, List<GtfsStop>>();
        foreach (var s in stops)
        {
            var key = GetGridKey(s.StopLat, s.StopLon);
            if (!grid.TryGetValue(key, out var list))
            {
                list = new List<GtfsStop>();
                grid[key] = list;
            }
            list.Add(s);
        }

        var transfersDict = new System.Collections.Concurrent.ConcurrentDictionary<string, GtfsTransfer>();
        int calculatedCount = 0;

        _logger.LogInformation("Spatial grid built. Calculating transfers with radius {MaxMeters}m using parallel processing.", maxWalkMeters);

        await Parallel.ForEachAsync(stops, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, async (originStop, ct) =>
        {
            var originLat = originStop.StopLat;
            var originLon = originStop.StopLon;
            
            var neighborKeys = GetNeighborKeys(originLat, originLon);
            foreach (var key in neighborKeys)
            {
                if (grid.TryGetValue(key, out var neighborStops))
                {
                    foreach (var targetStop in neighborStops)
                    {
                        if (originStop.StopId == targetStop.StopId) continue;

                        double distance = Haversine(originLat, originLon, targetStop.StopLat, targetStop.StopLon);
                        
                        // Strict data integrity validation for graph edges
                        if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0) continue;
                        
                        if (distance <= maxWalkMeters)
                        {
                            bool isSamePhysical = distance < 10.0;
                            
                            var transfer = new GtfsTransfer
                            {
                                GtfsImportRunId = gtfsImportRunId,
                                FromStopId = originStop.StopId,
                                ToStopId = targetStop.StopId,
                                DistanceMeters = distance,
                                WalkingTimeSeconds = (int)Math.Ceiling(distance / walkingSpeed),
                                IsSamePhysicalStop = isSamePhysical,
                                IsSameParentStation = false, // Varsa ParentStationId mantığı eklenebilir
                                IsSameCoordinateCluster = isSamePhysical,
                                CalculationMethod = "Haversine",
                                CreatedAt = DateTime.UtcNow
                            };
                            
                            var edgeKey = $"{originStop.StopId}_{targetStop.StopId}";
                            transfersDict[edgeKey] = transfer;
                        }
                    }
                }
            }
            
            Interlocked.Increment(ref calculatedCount);
            if (calculatedCount % 1000 == 0)
            {
                _logger.LogInformation("Processed {Count}/{Total} stops...", calculatedCount, stops.Count);
            }
        });

        return transfersDict.Values.ToList();
    }

    private string GetGridKey(double lat, double lon)
    {
        return $"{Math.Round(lat, 2)}_{Math.Round(lon, 2)}";
    }

    private List<string> GetNeighborKeys(double lat, double lon)
    {
        var keys = new List<string>();
        double step = 0.01;

        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                keys.Add(GetGridKey(lat + (i * step), lon + (j * step)));
            }
        }
        return keys;
    }

    private double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        double r = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }
}
