using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using TransportDataService;
using ulasim_veri_servisi.Exceptions;
using ulasim_veri_servisi.Services;

namespace ulasim_veri_servisi.Controllers
{
    [ApiController]
    [Route("api/v1/stops")]
    public class StopsController : ControllerBase
    {
        private readonly ApproachingBusService _approachingBusService;
        private readonly AppDbContext _context;

        public StopsController(
       AppDbContext context,
       ApproachingBusService approachingBusService)
        {
            _context = context;
            _approachingBusService = approachingBusService;
        }

  
     
        [HttpGet]
        [SwaggerOperation(
    Summary = "Durakları listeler",
    Description = "Arama, hat numarası filtreleme ve sayfalama desteğiyle durak listesini döndürür."
)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetStops(

    string? search,
    string? routeNumber,
    string? sort,
    string? order,
    int page = 1,
    int pageSize = 20)


        {
            search = search?.Trim();
            routeNumber = routeNumber?.Trim();
            sort = sort?.Trim();
            order = order?.Trim();
            if (page < 1)
            {
                return Problem(detail: "page değeri en az 1 olmalıdır.", statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz parametre");
            }

            if (pageSize < 1 || pageSize > 100)
            {
                return Problem(detail: "pageSize 1 ile 100 arasında olmalıdır.", statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz parametre");
            }
            var query = _context.Stops
                .AsNoTracking()
                .Include(x => x.StopRoutes)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(routeNumber))
            {
                query = query.Where(x =>
    x.StopRoutes.Any(r =>
        r.RouteNumber.ToLower() == routeNumber!.ToLower()));
            }


            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(x =>
    x.Name.ToLower().Contains(search!.ToLower()));
            }
            if (!string.IsNullOrWhiteSpace(sort))
            {
                switch (sort.ToLower())
                {
                    case "name":
                        query = order?.ToLower() == "desc"
                            ? query.OrderByDescending(x => x.Name)
                            : query.OrderBy(x => x.Name);
                        break;

                    case "externalstopid":
                        query = order?.ToLower() == "desc"
                            ? query.OrderByDescending(x => x.ExternalStopId)
                            : query.OrderBy(x => x.ExternalStopId);
                        break;

                    case "id":
                    default:
                        query = order?.ToLower() == "desc"
                            ? query.OrderByDescending(x => x.Id)
                            : query.OrderBy(x => x.Id);
                        break;
                }
            }
            else
            {
                query = query.OrderBy(x => x.Id);
            }

            var totalCount = query.Count();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var items = query
     .Skip((page - 1) * pageSize)
     .Take(pageSize)
     .Select(x => new
     {
         id = x.Id,
         externalStopId = x.ExternalStopId,
         name = x.Name,
         latitude = x.Latitude,
         longitude = x.Longitude,
         routes = x.StopRoutes.Select(r => r.RouteNumber).ToList()
     })
     .ToList();

            return Ok(new
            {
                items,
                page,
                pageSize,
                totalCount,
                totalPages,
                hasNextPage = page < totalPages,
                hasPreviousPage = page > 1
            });
        }
        [HttpGet("{id}")]
        [SwaggerOperation(
    Summary = "Id'ye göre durak getirir",
    Description = "Veritabanındaki Id değerine göre durak bilgisini döndürür."
)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetStopById(int id)
        {
            var stop = _context.Stops
                .AsNoTracking()
                .Include(x => x.StopRoutes)
                .FirstOrDefault(x => x.Id == id);

            if (stop == null)
            {
                return Problem(detail: "İstenen kaynak bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");
            }

            return Ok(new
            {
                id = stop.Id,
                externalStopId = stop.ExternalStopId,
                name = stop.Name,
                latitude = stop.Latitude,
                longitude = stop.Longitude,
                routes = stop.StopRoutes.Select(x => x.RouteNumber).ToList()
            });
        }
        [HttpGet("by-external-id/{externalStopId}")]
        [SwaggerOperation(
    Summary = "Gerçek durak numarasına göre durak getirir",
    Description = "ESHOT durak numarasına göre durak bilgisini döndürür."
)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetStopByExternalId(string externalStopId)
        {
            var stop = _context.Stops
                .AsNoTracking()
                .Include(x => x.StopRoutes)
                .FirstOrDefault(x => x.ExternalStopId == externalStopId);

            if (stop == null)
            {
                return Problem(detail: "İstenen kaynak bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "Kaynak bulunamadı");
            }

            return Ok(new
            {
                id = stop.Id,
                externalStopId = stop.ExternalStopId,
                name = stop.Name,
                latitude = stop.Latitude,
                longitude = stop.Longitude,
                routes = stop.StopRoutes.Select(x => x.RouteNumber).ToList()
            });
        }
        [HttpGet("nearby")]
        [SwaggerOperation(
    Summary = "Yakındaki durakları getirir",
    Description = "Verilen koordinata yakın durakları Haversine formülü ile hesaplar."
)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetNearbyStops(
    double latitude,
    double longitude,
    double radiusMeters)
        {
            if (radiusMeters <= 0)
            {
                return Problem(detail: "radiusMeters sıfırdan büyük olmalıdır.", statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz parametre");
            }
            var items = _context.Stops
                .AsNoTracking()
                .ToList()
               .Select(stop => new
               {
                   id = stop.Id,
                   externalStopId = stop.ExternalStopId,
                   name = stop.Name,
                   latitude = stop.Latitude,
                   longitude = stop.Longitude,
                   distanceMeters =
        stop.Latitude.HasValue && stop.Longitude.HasValue
            ? CalculateDistance(
                latitude,
                longitude,
                stop.Latitude.Value,
                stop.Longitude.Value)
            : (double?)null
               })
                .Where(x => x.distanceMeters <= radiusMeters)
                .OrderBy(x => x.distanceMeters)
                .ToList();

            return Ok(new
            {
                items
            });
        }
        private static double CalculateDistance(
    double lat1,
    double lon1,
    double lat2,
    double lon2)
        {
            const double R = 6371000;

            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) *
                Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        [HttpGet("{id}/approaching-buses")]
        [SwaggerOperation(
    Summary = "Durağa yaklaşan otobüsleri getirir",
    Description = "Veritabanındaki durak Id'sine göre ESHOT API'den yaklaşan otobüsleri döndürür."
)]
        [ProducesResponseType(typeof(ApproachingBusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetApproachingBuses([FromRoute] int id)
        {
            var result = await _approachingBusService.GetApproachingBusesAsync(id);

            return Ok(result);
        }
    }


}
