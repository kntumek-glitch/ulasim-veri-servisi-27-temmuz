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
                else if (!string.IsNullOrEmpty(dbRoute.RouteLongName))
                {
                    var parts = dbRoute.RouteLongName.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        headsign0 = parts[0].Trim();
                        headsign1 = parts[1].Trim();
                    }
                    else
                    {
                        headsign0 = dbRoute.RouteLongName;
                        headsign1 = dbRoute.RouteLongName;
                    }
                }
                
                if (!string.IsNullOrEmpty(hs1)) headsign1 = hs1;
            }

            var shapePoints = new List<TransportDataService.Domain.GtfsShapePoint>();
            if (dbRoute != null)
            {
                var shapeIds = await _context.GtfsTrips
                    .Where(t => t.RouteId == dbRouteId && t.ShapeId != null)
                    .Select(t => t.ShapeId)
                    .Distinct()
                    .Take(4)
                    .ToListAsync(cancellationToken);

                if (shapeIds.Count > 0)
                {
                    shapePoints = await _context.GtfsShapePoints
                        .Where(sp => shapeIds.Contains(sp.ShapeId))
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);
                }
            }

            foreach (var bus in uniqueBuses)
            { 
                var rawX = CoordinateParser.ParseNullable(bus.KoorX, -180, 180);
                var rawY = CoordinateParser.ParseNullable(bus.KoorY, -180, 180);
                var corrected = CoordinateParser.AutoCorrectIzmirCoordinates(rawX, rawY);

                if (corrected.Latitude == null || corrected.Longitude == null)
                    continue;

                // Check distance to route shape
                if (shapePoints.Count > 0)
                {
                    double minDistance = double.MaxValue;
                    foreach (var sp in shapePoints)
                    {
                        var dist = GeoUtils.CalculateDistance(corrected.Latitude.Value, corrected.Longitude.Value, sp.Latitude, sp.Longitude);
                        if (dist < minDistance) minDistance = dist;
                    }
                    
                    if (minDistance > 250) // 250 meters filter
                    {
                        continue; // Skip this bus, it's too far from the route
                    }
                }
                
                string destination = bus.Yon == 0 ? headsign0 : headsign1;
                string locationCtx = await _geocodeService.GetLocationContextAsync(corrected.Latitude, corrected.Longitude, cancellationToken);
                
                // Estimate departure time by finding the closest scheduled trip today before now
                string depTime = "Bilinmiyor";
                if (!string.IsNullOrEmpty(dbRouteId))
                {
                    var nowTimeStr = DateTime.Now.ToString("HH:mm:ss");
                    var trip = await _context.GtfsStopTimes
                        .Where(st => st.Trip.RouteId == dbRouteId && st.Trip.DirectionId == bus.Yon && st.StopSequence == 1 && st.DepartureTimeRaw != null && string.Compare(st.DepartureTimeRaw, nowTimeStr) <= 0)
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
                    Direction = bus.Yon.ToString(),
                    Latitude = corrected.Latitude,
                    Longitude = corrected.Longitude,
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
