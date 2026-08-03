using System.Globalization;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasim_veri_servisi.Helpers;

namespace ulasim_veri_servisi.Services
{
    public class RouteVehiclesService
    {
        private readonly AppDbContext _context;
        private readonly IExternalEshotService _externalEshotService;

        public RouteVehiclesService(
      AppDbContext context,
      IExternalEshotService externalEshotService)
        {
            _context = context;
            _externalEshotService = externalEshotService;
        }
        public async Task<RouteVehiclesResponse> GetRouteVehiclesAsync(string routeNumber, CancellationToken cancellationToken = default)
        {
            var cacheResult = await _externalEshotService.GetRouteVehiclesAsync(routeNumber, cancellationToken);


            var result = new RouteVehiclesResponse
            {
                RouteNumber = routeNumber,
                RetrievedAt = DateTime.UtcNow,
                FromCache = cacheResult.FromCache
            }; ;

            foreach (var bus in cacheResult.Data)
            { 
                result.Vehicles.Add(new RouteVehicleItem
                {
                    BusId = bus.OtobusId.ToString(),
                    Direction = bus.Yon.ToString(),

                    Latitude = CoordinateParser.ParseNullable(bus.KoorX, -90, 90),
                    Longitude = CoordinateParser.ParseNullable(bus.KoorY, -180, 180)
                });
            }
            return result;
        }
    }
}
