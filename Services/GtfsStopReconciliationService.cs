using Microsoft.EntityFrameworkCore;
using TransportDataService;
using TransportDataService.Domain;

namespace ulasım_veri_servisi.Services
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

        public async Task<ulasım_veri_servisi.Models.Gtfs.GtfsStopReconciliationResult> ReconcileAsync(
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

            int totalMatches = 0;

            int stopCodeMatches = 0;

            int missingInStops = 0;

            int missingInGtfs = 0;

            int nameMismatch = 0;

            int coordinateMismatch = 0;

            int manualReview = 0;

            var manualReviewStops = new List<string>();

            foreach (var gtfsStop in gtfsStops)
            {
                Stop? stop = null;

                stopIdDictionary.TryGetValue(
                    gtfsStop.StopId,
                    out stop);
                if (stop != null)
            {
                totalMatches++;
                    if (!string.Equals(
    stop.Name?.Trim(),
    gtfsStop.StopName?.Trim(),
    StringComparison.OrdinalIgnoreCase))
                    {
                        nameMismatch++;
                    }
                    if (stop.Latitude.HasValue &&
     stop.Longitude.HasValue)
                    {
                        var latitudeDifference =
                            Math.Abs(stop.Latitude.Value - gtfsStop.StopLat);

                        var longitudeDifference =
                            Math.Abs(stop.Longitude.Value - gtfsStop.StopLon);

                        if (latitudeDifference > 0.0001 ||
                            longitudeDifference > 0.0001)
                        {
                            coordinateMismatch++;
                        }
                    }
                }
                else
                {
                    stopIdDictionary.TryGetValue(
     gtfsStop.StopCode,
     out stop);

                    if (stop != null)
                    {
                        stopCodeMatches++;
                    }
                    else
                    {
                        missingInStops++;

                        Stop? similarStop = null;

                        var normalizedName = gtfsStop.StopName?.Trim().ToLower() ?? string.Empty;
                        stopNameDictionary.TryGetValue(
                            normalizedName,
                            out similarStop);

                        if (similarStop != null)
                        {
                            manualReview++;

                            manualReviewStops.Add(
     $"GTFS StopId={gtfsStop.StopId}, " +
     $"StopCode={gtfsStop.StopCode}, " +
     $"Name='{gtfsStop.StopName}' <-> " +
     $"Stops ExternalStopId={similarStop.ExternalStopId}, " +
     $"Name='{similarStop.Name}'");
                        }
                    }
                }
            }
            foreach (var stop in stops)
            {
                if (string.IsNullOrWhiteSpace(stop.ExternalStopId))
                {
                    continue;
                }

                var exists =
                    gtfsStopDictionary.ContainsKey(stop.ExternalStopId)
                    || gtfsStopCodeDictionary.ContainsKey(stop.ExternalStopId);

                if (!exists)
                {
                    missingInGtfs++;
                }
            }
          
            var report = $"""
# GTFS Stop Reconciliation
                  
Generated At
{ DateTime.UtcNow:u}

## Total Matches

{totalMatches}

## StopCode Matches

{stopCodeMatches}

## Missing In Stops

{missingInStops}

## Missing In GTFS

{missingInGtfs}

## Name Mismatch

{nameMismatch}

## Coordinate Mismatch

{coordinateMismatch}

## Manual Review

{manualReview}

### Records

{string.Join(Environment.NewLine, manualReviewStops)}
"""; 
            var docsFolder =
    Path.Combine(
        Directory.GetCurrentDirectory(),
        "docs");

            Directory.CreateDirectory(docsFolder);
            var reportPath =
    Path.Combine(
        docsFolder,
        "gtfs-stop-reconciliation.md");
            await File.WriteAllTextAsync(
    reportPath,
    report,
    cancellationToken);

            return new ulasım_veri_servisi.Models.Gtfs.GtfsStopReconciliationResult
            {
                TotalMatches = totalMatches,
                StopCodeMatches = stopCodeMatches,
                MissingInStops = missingInStops,
                MissingInGtfs = missingInGtfs,
                NameMismatches = nameMismatch,
                CoordinateMismatches = coordinateMismatch,
                ManualReview = manualReview
            };
        }
    }
}
