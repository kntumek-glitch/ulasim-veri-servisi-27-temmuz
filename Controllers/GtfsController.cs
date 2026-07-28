using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using TransportDataService;
using ulasım_veri_servisi.Models.Gtfs;

namespace ulasım_veri_servisi.Controllers;

[ApiController]
[Route("api/v1/gtfs")]
[ServiceFilter(typeof(ulasım_veri_servisi.Filters.GtfsETagCacheFilterAttribute))]
public class GtfsController : ControllerBase
{
    private readonly AppDbContext _context;

    public GtfsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("metadata")]
    public async Task<ActionResult<FeedMetadataDto>> GetFeedMetadata()
    {
        var run = await _context.GtfsImportRuns
            .Where(x => x.IsActive && x.Status == "Completed")
            .FirstOrDefaultAsync();

        if (run == null)
            return Problem(detail: "Aktif bir GTFS verisi bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        bool missingCalendarDates = !await _context.GtfsCalendarDates.AnyAsync();
        
        var missingFiles = new List<string>();
        if (missingCalendarDates) missingFiles.Add("calendar_dates.txt");

        bool isStale = false;
        if (run.FeedEndDate.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (run.FeedEndDate.Value < today)
                isStale = true;
        }

        var metadata = new FeedMetadataDto
        {
            ImportId = run.Id.ToString(),
            ImportDate = run.FinishedAt ?? run.DownloadedAt,
            FileHash = run.FileHash ?? string.Empty,
            FeedStartDate = run.FeedStartDate?.ToString("yyyy-MM-dd"),
            FeedEndDate = run.FeedEndDate?.ToString("yyyy-MM-dd"),
            IsStale = isStale,
            MissingFiles = missingFiles,
            DataVersion = run.FeedVersion ?? string.Empty
        };

        return Ok(metadata);
    }

    [HttpGet("feed-info")]
    public async Task<ActionResult<FeedInfoResponse>> GetFeedInfo()
    {
        var run = await _context.GtfsImportRuns
            .Where(x => x.IsActive && x.Status == "Completed")
            .FirstOrDefaultAsync();

        if (run == null)
            return Problem(detail: "No completed GTFS import found.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        var response = new FeedInfoResponse
        {
            FeedVersion = run.FeedVersion,
            FeedStartDate = run.FeedStartDate,
            FeedEndDate = run.FeedEndDate,
            DownloadedAt = run.DownloadedAt,
            ImportedAt = run.FinishedAt,
            FileHash = run.FileHash,
            Counts = new FeedCounts
            {
                AgencyCount = run.AgencyCount,
                RouteCount = run.RouteCount,
                StopCount = run.StopCount,
                TripCount = run.TripCount,
                StopTimeCount = run.StopTimeCount,
                ShapePointCount = run.ShapePointCount
            }
        };

        return Ok(response);
    }

    [HttpGet("stops")]
    public async Task<ActionResult<PaginatedResponse<GtfsStopResponse>>> GetGtfsStops(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.GtfsStops.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(stop =>
                EF.Functions.ILike(stop.StopId, pattern) ||
                EF.Functions.ILike(stop.StopCode, pattern) ||
                EF.Functions.ILike(stop.StopName, pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var stops = await query
            .OrderBy(stop => stop.StopName)
            .ThenBy(stop => stop.StopId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(stop => new GtfsStopResponse
            {
                StopId = stop.StopId,
                StopCode = stop.StopCode,
                StopName = stop.StopName,
                Latitude = stop.StopLat,
                Longitude = stop.StopLon,
                PlatformCode = stop.PlatformCode,
                LocationType = stop.LocationType,
                ParentStation = stop.ParentStation,
                DirectionIds = stop.StopTimes
                    .Where(stopTime => stopTime.Trip.DirectionId.HasValue)
                    .Select(stopTime => stopTime.Trip.DirectionId!.Value)
                    .Distinct()
                    .OrderBy(directionId => directionId)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<GtfsStopResponse>
        {
            Items = stops,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("stops/{stopId}")]
    public async Task<ActionResult<GtfsStopResponse>> GetGtfsStop(
        string stopId,
        CancellationToken cancellationToken)
    {
        var stop = await _context.GtfsStops
            .AsNoTracking()
            .Where(item => item.StopId == stopId)
            .Select(item => new GtfsStopResponse
            {
                StopId = item.StopId,
                StopCode = item.StopCode,
                StopName = item.StopName,
                Latitude = item.StopLat,
                Longitude = item.StopLon,
                PlatformCode = item.PlatformCode,
                LocationType = item.LocationType,
                ParentStation = item.ParentStation,
                DirectionIds = item.StopTimes
                    .Where(stopTime => stopTime.Trip.DirectionId.HasValue)
                    .Select(stopTime => stopTime.Trip.DirectionId!.Value)
                    .Distinct()
                    .OrderBy(directionId => directionId)
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (stop is null)
            return Problem(detail: "İstenen GTFS durağı bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        return Ok(stop);
    }

    [HttpGet("stops/{stopId}/routes")]
    public async Task<ActionResult<IEnumerable<GtfsStopRouteResponse>>> GetGtfsStopRoutes(
        string stopId,
        CancellationToken cancellationToken)
    {
        var exists = await _context.GtfsStops
            .AsNoTracking()
            .AnyAsync(stop => stop.StopId == stopId, cancellationToken);

        if (!exists)
            return Problem(detail: "İstenen GTFS durağı bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        var routes = await _context.GtfsStopTimes
            .AsNoTracking()
            .Where(stopTime => stopTime.Stop.StopId == stopId)
            .Select(stopTime => new
            {
                stopTime.Trip.RouteId,
                stopTime.Trip.Route.RouteShortName,
                stopTime.Trip.Route.RouteLongName,
                stopTime.Trip.DirectionId,
                stopTime.Trip.TripHeadsign
            })
            .Distinct()
            .OrderBy(route => route.RouteShortName)
            .ThenBy(route => route.RouteId)
            .ThenBy(route => route.DirectionId)
            .Select(route => new GtfsStopRouteResponse
            {
                RouteId = route.RouteId,
                RouteShortName = route.RouteShortName,
                RouteLongName = route.RouteLongName,
                DirectionId = route.DirectionId,
                TripHeadsign = route.TripHeadsign
            })
            .ToListAsync(cancellationToken);

        return Ok(routes);
    }

    [HttpGet("routes/{routeId}/patterns")]
    public async Task<ActionResult<IEnumerable<GtfsRoutePatternResponse>>> GetRoutePatterns(
        string routeId,
        [FromQuery] int directionId = 0,
        CancellationToken cancellationToken = default)
    {
        var routeExists = await _context.GtfsRoutes
            .AsNoTracking()
            .AnyAsync(route => route.RouteId == routeId, cancellationToken);

        if (!routeExists)
            return Problem(detail: "İstenen GTFS güzergâhı bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        var stopTimes = await _context.GtfsStopTimes
            .AsNoTracking()
            .Where(stopTime =>
                stopTime.Trip.RouteId == routeId &&
                stopTime.Trip.DirectionId == directionId)
            .OrderBy(stopTime => stopTime.Trip.TripId)
            .ThenBy(stopTime => stopTime.StopSequence)
            .Select(stopTime => new PatternStopRow
            {
                TripId = stopTime.Trip.TripId,
                ShapeId = stopTime.Trip.ShapeId,
                StopSequence = stopTime.StopSequence,
                StopId = stopTime.Stop.StopId,
                StopCode = stopTime.Stop.StopCode,
                StopName = stopTime.Stop.StopName,
                StopLat = stopTime.Stop.StopLat,
                StopLon = stopTime.Stop.StopLon,
                PlatformCode = stopTime.Stop.PlatformCode
            })
            .ToListAsync(cancellationToken);

        var trips = stopTimes
            .GroupBy(stopTime => new { stopTime.TripId, stopTime.ShapeId })
            .Select(group => new
            {
                group.Key.TripId,
                group.Key.ShapeId,
                Stops = group.OrderBy(stopTime => stopTime.StopSequence).ToList()
            })
            .Where(trip => trip.Stops.Count > 0)
            .ToList();

        var patterns = trips
            .GroupBy(trip => string.Join('\u001F', trip.Stops.Select(stop => stop.StopId)))
            .Select(group =>
            {
                var representative = group.OrderBy(trip => trip.TripId).First();
                var start = representative.Stops.First();
                var end = representative.Stops.Last();
                var patternHash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes($"{routeId}|{directionId}|{group.Key}")));

                var rawPatternId = $"{routeId}|{directionId}|{patternHash[..16]}";
                var encodedPatternId = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawPatternId));

                return new GtfsRoutePatternResponse
                {
                    PatternId = encodedPatternId,
                    RouteId = routeId,
                    DirectionId = directionId,
                    RepresentativeTripId = representative.TripId,
                    ShapeId = representative.ShapeId,
                    TripCount = group.Count(),
                    StopCount = representative.Stops.Count,
                    StartStop = ToPatternEndpointStop(start),
                    EndStop = ToPatternEndpointStop(end)
                };
            })
            .OrderBy(pattern => pattern.PatternId)
            .ToList();

        return Ok(patterns);
    }

    [HttpGet("routes")]
    public async Task<ActionResult<PaginatedResponse<RouteDto>>> GetRoutes(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _context.GtfsRoutes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => 
                r.RouteShortName.Contains(search) || 
                (r.RouteLongName != null && r.RouteLongName.Contains(search)));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(r => r.RouteId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RouteDto
            {
                RouteId = r.RouteId,
                AgencyId = r.AgencyId,
                RouteShortName = r.RouteShortName,
                RouteLongName = r.RouteLongName,
                RouteDesc = r.RouteDesc,
                RouteType = r.RouteType,
                RouteColor = r.RouteColor,
                RouteTextColor = r.RouteTextColor
            })
            .ToListAsync();

        return Ok(new PaginatedResponse<RouteDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    [HttpGet("routes/{routeId}")]
    public async Task<ActionResult<RouteDto>> GetRoute(string routeId)
    {
        var route = await _context.GtfsRoutes
            .Where(r => r.RouteId == routeId)
            .Select(r => new RouteDto
            {
                RouteId = r.RouteId,
                AgencyId = r.AgencyId,
                RouteShortName = r.RouteShortName,
                RouteLongName = r.RouteLongName,
                RouteDesc = r.RouteDesc,
                RouteType = r.RouteType,
                RouteColor = r.RouteColor,
                RouteTextColor = r.RouteTextColor
            })
            .FirstOrDefaultAsync();

        if (route == null)
            return Problem(detail: "İstenen kaynak bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        return Ok(route);
    }

    [HttpGet("routes/{routeId}/directions")]
    public async Task<ActionResult<RouteDirectionsResponse>> GetRouteDirections(string routeId)
    {
        var hasRoute = await _context.GtfsRoutes.AnyAsync(r => r.RouteId == routeId);
        if (!hasRoute)
            return Problem(detail: "İstenen kaynak bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        var directionGroups = await _context.GtfsTrips
            .Where(t => t.RouteId == routeId && t.DirectionId.HasValue)
            .GroupBy(t => t.DirectionId!.Value)
            .Select(g => new
            {
                DirectionId = g.Key,
                Headsigns = g.Where(t => t.TripHeadsign != null).Select(t => t.TripHeadsign!).Distinct().ToList()
            })
            .ToListAsync();

        var response = new RouteDirectionsResponse
        {
            RouteId = routeId,
            Directions = directionGroups.Select(dg => new DirectionInfo
            {
                DirectionId = dg.DirectionId,
                Headsigns = dg.Headsigns
            }).ToList()
        };

        return Ok(response);
    }

    [HttpGet("routes/{routeId}/stops")]
    public async Task<ActionResult<IEnumerable<RouteStopDto>>> GetRouteStops(string routeId, [FromQuery] int directionId = 0)
    {
        // Find the trip with the most stops for the given route and direction to use as representative
        var representativeTripId = await _context.GtfsTrips
            .Where(t => t.RouteId == routeId && t.DirectionId == directionId)
            .Select(t => new { t.Id, StopCount = t.StopTimes.Count })
            .OrderByDescending(t => t.StopCount)
            .Select(t => t.Id)
            .FirstOrDefaultAsync();

        if (representativeTripId == 0)
            return Problem(detail: "No trips found for the given route and direction.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        var stopTimes = await _context.GtfsStopTimes
            .Include(st => st.Stop)
            .Where(st => st.GtfsTripId == representativeTripId)
            .OrderBy(st => st.StopSequence)
            .Select(st => new RouteStopDto
            {
                StopId = st.Stop.StopId,
                StopCode = st.Stop.StopCode,
                StopName = st.Stop.StopName,
                Latitude = st.Stop.StopLat,
                Longitude = st.Stop.StopLon,
                StopSequence = st.StopSequence
            })
            .ToListAsync();

        return Ok(stopTimes);
    }

    [HttpGet("routes/{routeId}/shape")]
    public async Task<ActionResult<IEnumerable<ShapePointDto>>> GetRouteShape(string routeId, [FromQuery] int directionId = 0)
    {
        // First try to find a representative trip that has a ShapeId for the route and direction
        var shapeId = await _context.GtfsTrips
            .Where(t => t.RouteId == routeId && t.DirectionId == directionId && t.ShapeId != null)
            .Select(t => t.ShapeId)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(shapeId))
            return Problem(detail: "No shape geometry found for the given route and direction.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");

        var shapePoints = await _context.GtfsShapePoints
            .Where(sp => sp.ShapeId == shapeId)
            .OrderBy(sp => sp.Sequence)
            .Select(sp => new ShapePointDto
            {
                Latitude = sp.Latitude,
                Longitude = sp.Longitude,
                Sequence = sp.Sequence
            })
            .ToListAsync();

        return Ok(shapePoints);
    }

    private static GtfsPatternEndpointStop ToPatternEndpointStop(PatternStopRow stop) => new()
    {
        StopId = stop.StopId,
        StopCode = stop.StopCode,
        StopName = stop.StopName,
        Latitude = stop.StopLat,
        Longitude = stop.StopLon,
        PlatformCode = stop.PlatformCode
    };

    private sealed class PatternStopRow
    {
        public string TripId { get; init; } = string.Empty;
        public string? ShapeId { get; init; }
        public int StopSequence { get; init; }
        public string StopId { get; init; } = string.Empty;
        public string StopCode { get; init; } = string.Empty;
        public string StopName { get; init; } = string.Empty;
        public double StopLat { get; init; }
        public double StopLon { get; init; }
        public string? PlatformCode { get; init; }
    }

    [HttpGet("shapes")]
    public async Task<ActionResult<GeoJsonShapeResponseDto>> GetShapes(
        [FromQuery] string? tripId,
        [FromQuery] string? patternId,
        [FromQuery] string format = "json",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tripId) && string.IsNullOrWhiteSpace(patternId))
        {
            return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Bad Request", Detail = "Lütfen tripId veya patternId parametrelerinden en az birini gönderiniz." });
        }

        string? targetShapeId = null;
        string? matchedTripId = tripId;

        if (!string.IsNullOrWhiteSpace(tripId))
        {
            var trip = await _context.GtfsTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripId == tripId, cancellationToken);

            if (trip == null)
                return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not Found", Detail = "Belirtilen tripId sistemde bulunamadı." });

            targetShapeId = trip.ShapeId;
        }
        else if (!string.IsNullOrWhiteSpace(patternId))
        {
            string rawPatternId;
            try
            {
                rawPatternId = Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(patternId));
            }
            catch (FormatException)
            {
                return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Invalid PatternId", Detail = "Geçersiz patternId formatı." });
            }

            var parts = rawPatternId.Split('|');
            if (parts.Length < 3)
                return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Invalid PatternId", Detail = "Geçersiz patternId formatı." });

            var routeId = parts[0];
            if (!int.TryParse(parts[1], out int directionId))
                return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Invalid DirectionId", Detail = "Geçersiz patternId (directionId okunamadı)." });

            // Generate patterns for this route and direction to find the matching one
            var stopTimes = await _context.GtfsStopTimes
                .AsNoTracking()
                .Where(stopTime =>
                    stopTime.Trip.RouteId == routeId &&
                    stopTime.Trip.DirectionId == directionId)
                .OrderBy(stopTime => stopTime.Trip.TripId)
                .ThenBy(stopTime => stopTime.StopSequence)
                .Select(stopTime => new 
                {
                    stopTime.Trip.TripId,
                    stopTime.Trip.ShapeId,
                    stopTime.StopSequence,
                    stopTime.Stop.StopId,
                })
                .ToListAsync(cancellationToken);

            var trips = stopTimes
                .GroupBy(stopTime => new { stopTime.TripId, stopTime.ShapeId })
                .Select(group => new
                {
                    group.Key.TripId,
                    group.Key.ShapeId,
                    Stops = group.OrderBy(stopTime => stopTime.StopSequence).Select(s => s.StopId).ToList()
                })
                .Where(trip => trip.Stops.Count > 0)
                .ToList();

            var patternFound = trips
                .GroupBy(trip => string.Join('\u001F', trip.Stops))
                .Select(group =>
                {
                    var representative = group.OrderBy(trip => trip.TripId).First();
                    var patternHash = Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes($"{routeId}|{directionId}|{group.Key}")));
                    
                    var calcRaw = $"{routeId}|{directionId}|{patternHash[..16]}";
                    var calculatedPatternId = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(calcRaw));
                    
                    return new { PatternId = calculatedPatternId, representative.ShapeId, representative.TripId };
                })
                .FirstOrDefault(p => p.PatternId == patternId);

            if (patternFound == null)
                return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not Found", Detail = "Belirtilen patternId sistemde bulunamadı." });

            targetShapeId = patternFound.ShapeId;
            matchedTripId = patternFound.TripId;
        }

        if (string.IsNullOrWhiteSpace(targetShapeId))
            return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not Found", Detail = "İlgili sefer veya pattern için Shape verisi (ShapeId) bulunamadı." });

        var shapePoints = await _context.GtfsShapePoints
            .AsNoTracking()
            .Where(sp => sp.ShapeId == targetShapeId)
            .OrderBy(sp => sp.Sequence)
            .Select(sp => new ShapeCoordinateDto
            {
                Lat = sp.Latitude,
                Lon = sp.Longitude,
                Sequence = sp.Sequence
            })
            .ToListAsync(cancellationToken);

        if (shapePoints.Count == 0)
            return Problem(detail: "ShapeId veritabanında mevcut, ancak koordinat verisi bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak Bulunamadı");

        var response = new GeoJsonShapeResponseDto
        {
            ShapeId = targetShapeId,
            TripId = matchedTripId,
            PatternId = patternId,
            Coordinates = shapePoints
        };

        if (format.Equals("geojson", StringComparison.OrdinalIgnoreCase))
        {
            var geoJson = new GeoJsonFeature();
            foreach (var point in shapePoints)
            {
                geoJson.Geometry.Coordinates.Add(new double[] { point.Lon, point.Lat });
            }
            response.GeoJson = geoJson;
        }

        return Ok(response);
    }
}
