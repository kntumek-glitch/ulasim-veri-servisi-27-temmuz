using Microsoft.EntityFrameworkCore;
using TransportDataService;
using TransportDataService.Domain;

namespace ulasim_veri_servisi.Services
{
    public class GtfsStopReconciliationService
        : IGtfsStopReconciliationService
    {
        private readonly AppDbContext _context;

        public GtfsStopReconciliationService(
            AppDbContext context)
        {
            _context = context;
        }

        public async Task<ulasim_veri_servisi.Models.Gtfs.GtfsStopReconciliationResult> ReconcileAsync(
            CancellationToken cancellationToken)
        {
            var stops =
                await _context.Stops.ToListAsync(
                    cancellationToken);

            var gtfsStops =
                await _context.GtfsStops.ToListAsync(
                    cancellationToken);

            var stopIdDictionary =
    stops
        .Where(x => !string.IsNullOrWhiteSpace(x.ExternalStopId))
        .GroupBy(x => x.ExternalStopId)
        .ToDictionary(
            x => x.Key,
            x => x.First());

            var stopNameDictionary =
                stops
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => x.Name.Trim().ToLower())
                    .ToDictionary(
                        x => x.Key,
                        x => x.First());

            var gtfsStopDictionary =
    gtfsStops
        .Where(x => !string.IsNullOrWhiteSpace(x.StopId))
        .GroupBy(x => x.StopId)
        .ToDictionary(
            x => x.Key,
            x => x.First());

            var gtfsStopCodeDictionary =
                gtfsStops
                    .Where(x => !string.IsNullOrWhiteSpace(x.StopCode))
                    .GroupBy(x => x.StopCode)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First());

            int exactMatches = 0;
            int stopIdMatchesOnly = 0;
            int stopCodeMatchesOnly = 0;
            int onlyInGtfs = 0;
            int onlyInStops = 0;
            int nameMismatches = 0;
            int coordinateMismatches = 0;
            int manualReview = 0;

            var manualReviewStops = new List<string>();

            foreach (var gtfsStop in gtfsStops)
            {
                Stop? stop = null;

                stopIdDictionary.TryGetValue(gtfsStop.StopId, out stop);
                if (stop != null)
                {
                    bool hasNameMismatch = false;
                    bool hasCoordMismatch = false;

                    if (!string.Equals(stop.Name?.Trim(), gtfsStop.StopName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        nameMismatches++;
                        hasNameMismatch = true;
                    }

                    if (stop.Latitude.HasValue && stop.Longitude.HasValue)
                    {
                        var latitudeDifference = Math.Abs(stop.Latitude.Value - gtfsStop.StopLat);
                        var longitudeDifference = Math.Abs(stop.Longitude.Value - gtfsStop.StopLon);

                        if (latitudeDifference > 0.0001 || longitudeDifference > 0.0001)
                        {
                            coordinateMismatches++;
                            hasCoordMismatch = true;
                        }
                    }

                    if (!hasNameMismatch && !hasCoordMismatch)
                    {
                        exactMatches++;
                    }
                    else
                    {
                        stopIdMatchesOnly++;
                    }
                }
                else
                {
                    stopIdDictionary.TryGetValue(gtfsStop.StopCode, out stop);

                    if (stop != null)
                    {
                        stopCodeMatchesOnly++;
                    }
                    else
                    {
                        onlyInGtfs++;

                        Stop? similarStop = null;
                        var normalizedName = gtfsStop.StopName?.Trim().ToLower() ?? string.Empty;
                        stopNameDictionary.TryGetValue(normalizedName, out similarStop);

                        if (similarStop != null)
                        {
                            manualReview++;
                            manualReviewStops.Add($"GTFS StopId={gtfsStop.StopId}, StopCode={gtfsStop.StopCode}, Name='{gtfsStop.StopName}' <-> Stops ExternalStopId={similarStop.ExternalStopId}, Name='{similarStop.Name}'");
                        }
                    }
                }
            }

            foreach (var stop in stops)
            {
                if (string.IsNullOrWhiteSpace(stop.ExternalStopId)) continue;

                var exists = gtfsStopDictionary.ContainsKey(stop.ExternalStopId) || gtfsStopCodeDictionary.ContainsKey(stop.ExternalStopId);
                if (!exists)
                {
                    onlyInStops++;
                }
            }
          
            var report = $"""
# GTFS Stop Reconciliation (Güncel Metrikler)
                  
Oluşturulma Zamanı: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

- **Doğrudan eşleşenler:** {exactMatches}
- **Yalnızca Stop ID ile eşleşenler:** {stopIdMatchesOnly}
- **Yalnızca Stop Code ile eşleşenler:** {stopCodeMatchesOnly}
- **Yalnızca GTFS'te bulunanlar:** {onlyInGtfs}
- **Yalnızca eski Stops tablosunda bulunanlar:** {onlyInStops}
- **İsim farkı bulunanlar:** {nameMismatches}
- **Koordinat farkı bulunanlar:** {coordinateMismatches}
- **Manuel inceleme gerekenler:** {manualReview}

### Manuel İnceleme Gereken Kayıtlar

{string.Join(Environment.NewLine, manualReviewStops)}
"""; 
            var docsFolder = Path.Combine(Directory.GetCurrentDirectory(), "docs");
            Directory.CreateDirectory(docsFolder);
            var reportPath = Path.Combine(docsFolder, "gtfs-stop-reconciliation.md");
            await File.WriteAllTextAsync(reportPath, report, cancellationToken);

            return new ulasim_veri_servisi.Models.Gtfs.GtfsStopReconciliationResult
            {
                ExactMatches = exactMatches,
                StopIdMatchesOnly = stopIdMatchesOnly,
                StopCodeMatchesOnly = stopCodeMatchesOnly,
                OnlyInGtfs = onlyInGtfs,
                OnlyInStops = onlyInStops,
                NameMismatches = nameMismatches,
                CoordinateMismatches = coordinateMismatches,
                ManualReview = manualReview
            };
        }
    }
}

