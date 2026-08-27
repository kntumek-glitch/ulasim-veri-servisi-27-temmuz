using ulasim_veri_servisi.Models.External;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportDataService;
using TransportDataService.Domain;
using ulasim_veri_servisi.Helpers;

namespace ulasim_veri_servisi.Services
{
    public class RouteVehiclesService
    {
        private readonly AppDbContext _context;
        private readonly IExternalEshotService _externalEshotService;
        private readonly ReverseGeocodeService _geocodeService;

        public RouteVehiclesService(
            AppDbContext context,
            IExternalEshotService externalEshotService,
            ReverseGeocodeService geocodeService)
        {
            _context = context;
            _externalEshotService = externalEshotService;
            _geocodeService = geocodeService;
        }
        public async Task<RouteVehiclesResponse> GetRouteVehiclesAsync(string routeNumber, CancellationToken cancellationToken = default)
        {
            var cacheResult = await _externalEshotService.GetRouteVehiclesAsync(routeNumber, cancellationToken);

            // Fetch Route and Headsigns from DB
            var dbRoute = await _context.GtfsRoutes
                .Where(r => r.RouteShortName == routeNumber)
                .Select(r => new { r.RouteId, r.RouteLongName })
                .FirstOrDefaultAsync(cancellationToken);
            
            string dbRouteId = dbRoute?.RouteId;

            var result = new RouteVehiclesResponse
            {
                RouteId = dbRouteId ?? string.Empty,
                RouteNumber = routeNumber,
                RetrievedAt = DateTime.UtcNow,
                FromCache = cacheResult.FromCache
            };

            var uniqueBuses = cacheResult.Data
                .GroupBy(b => b.OtobusId)
                .Select(g => g.Last())
                .ToList();

            string headsign0 = "Bilinmeyen Yön";
            string headsign1 = "Bilinmeyen Yön";

            if (dbRoute != null)
            {
                var hs0 = await _context.GtfsTrips
                    .Where(t => t.RouteId == dbRouteId && t.DirectionId == 0 && t.TripHeadsign != null && t.TripHeadsign != "")
                    .Select(t => t.TripHeadsign)
                    .FirstOrDefaultAsync(cancellationToken);

                var hs1 = await _context.GtfsTrips
                    .Where(t => t.RouteId == dbRouteId && t.DirectionId == 1 && t.TripHeadsign != null && t.TripHeadsign != "")
                    .Select(t => t.TripHeadsign)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!string.IsNullOrEmpty(hs0)) headsign0 = hs0;
                else
                {
                    // Fallback to the last stop of Direction 0
                    var lastStop = await _context.GtfsStopTimes
                        .Where(st => st.Trip.RouteId == dbRouteId && st.Trip.DirectionId == 0)
                        .OrderByDescending(st => st.StopSequence)
                        .Select(st => st.Stop.StopName)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (!string.IsNullOrEmpty(lastStop)) headsign0 = lastStop;
                }
                
                if (!string.IsNullOrEmpty(hs1)) headsign1 = hs1;
                else
                {
                    // Fallback to the last stop of Direction 1
                    var lastStop = await _context.GtfsStopTimes
                        .Where(st => st.Trip.RouteId == dbRouteId && st.Trip.DirectionId == 1)
                        .OrderByDescending(st => st.StopSequence)
                        .Select(st => st.Stop.StopName)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (!string.IsNullOrEmpty(lastStop)) headsign1 = lastStop;
                }
            }

            var shapePoints0 = new List<TransportDataService.Domain.GtfsShapePoint>();
            var shapePoints1 = new List<TransportDataService.Domain.GtfsShapePoint>();
            if (dbRoute != null)
            {
                var shapeId0 = await _context.GtfsTrips
                    .Where(t => t.RouteId == dbRouteId && t.DirectionId == 0 && t.ShapeId != null)
                    .Select(t => t.ShapeId)
                    .FirstOrDefaultAsync(cancellationToken);
                    
                var shapeId1 = await _context.GtfsTrips
                    .Where(t => t.RouteId == dbRouteId && t.DirectionId == 1 && t.ShapeId != null)
                    .Select(t => t.ShapeId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (shapeId0 != null)
                    shapePoints0 = await _context.GtfsShapePoints.Where(sp => sp.ShapeId == shapeId0).AsNoTracking().ToListAsync(cancellationToken);
                if (shapeId1 != null)
                    shapePoints1 = await _context.GtfsShapePoints.Where(sp => sp.ShapeId == shapeId1).AsNoTracking().ToListAsync(cancellationToken);
            }

            // Spatial voting to map Yon to DirectionId
            int yon1VotesFor0 = 0;
            int yon1VotesFor1 = 0;
            
            var validBuses = new List<(ulasim_veri_servisi.Models.External.RouteVehicleDto Bus, double Lat, double Lon, double MinDist0, double MinDist1)>();

            foreach (var bus in uniqueBuses)
            { 
                var rawX = CoordinateParser.ParseNullable(bus.KoorX, -180, 180);
                var rawY = CoordinateParser.ParseNullable(bus.KoorY, -180, 180);
                var corrected = CoordinateParser.AutoCorrectIzmirCoordinates(rawX, rawY);

                if (corrected.Latitude == null || corrected.Longitude == null)
                    continue;

                double minDistance0 = double.MaxValue;
                foreach (var sp in shapePoints0)
                {
                    var dist = GeoUtils.CalculateDistance(corrected.Latitude.Value, corrected.Longitude.Value, sp.Latitude, sp.Longitude);
                    if (dist < minDistance0) minDistance0 = dist;
                }
                
                double minDistance1 = double.MaxValue;
                foreach (var sp in shapePoints1)
                {
                    var dist = GeoUtils.CalculateDistance(corrected.Latitude.Value, corrected.Longitude.Value, sp.Latitude, sp.Longitude);
                    if (dist < minDistance1) minDistance1 = dist;
                }
                
                double overallMin = Math.Min(minDistance0, minDistance1);
                if (overallMin > 250 && (shapePoints0.Count > 0 || shapePoints1.Count > 0))
                    continue; // Skip buses too far from any route shape
                    
                validBuses.Add((bus, corrected.Latitude.Value, corrected.Longitude.Value, minDistance0, minDistance1));
                
                if (bus.Yon == 1)
                {
                    if (minDistance0 < minDistance1) yon1VotesFor0++;
                    else if (minDistance1 < minDistance0) yon1VotesFor1++;
                }
            }

            bool yon1MapsTo0 = yon1VotesFor0 >= yon1VotesFor1;

            foreach (var item in validBuses)
            {
                var bus = item.Bus;
                int mappedDirection = bus.Yon == 1 ? (yon1MapsTo0 ? 0 : 1) : (yon1MapsTo0 ? 1 : 0);
                
                // If shape data was completely missing, fallback to raw 1->0 mapping
                if (shapePoints0.Count == 0 && shapePoints1.Count == 0)
                {
                    mappedDirection = bus.Yon == 1 ? 0 : 1;
                }
                
                string destination = mappedDirection == 0 ? headsign0 : headsign1;
                string locationCtx = await _geocodeService.GetLocationContextAsync(item.Lat, item.Lon, cancellationToken);
                
                // Estimate departure time by finding the closest scheduled trip today before now
                string depTime = "Bilinmiyor";
                if (!string.IsNullOrEmpty(dbRouteId))
                {
                    var nowTimeStr = DateTime.Now.ToString("HH:mm:ss");
                    var trip = await _context.GtfsStopTimes
                        .Where(st => st.Trip.RouteId == dbRouteId && st.Trip.DirectionId == mappedDirection && st.StopSequence == 1 && st.DepartureTimeRaw != null && string.Compare(st.DepartureTimeRaw, nowTimeStr) <= 0)
                        .OrderByDescending(st => st.DepartureTimeRaw)
                        .Select(st => st.DepartureTimeRaw)
                        .FirstOrDefaultAsync(cancellationToken);
                    
                    if (!string.IsNullOrEmpty(trip))
                    {
                        depTime = trip.Substring(0, 5); // HH:mm
                    }
                }

                result.Vehicles.Add(new RouteVehicleItem
                {
                    BusId = bus.OtobusId.ToString(),
                    Direction = mappedDirection.ToString(),
                    Latitude = item.Lat,
                    Longitude = item.Lon,
                    DestinationName = destination,
                    LocationContext = locationCtx,
                    OriginDepartureTime = depTime
                });
            }
            return result;
        }
    }

    public static class GeoUtils
    {
        public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var dLat = (lat2 - lat1) * Math.PI / 180.0;
            var dLon = (lon2 - lon1) * Math.PI / 180.0;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return 6371000 * c; // meters
        }
    }
}
