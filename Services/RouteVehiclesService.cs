using System.Globalization;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;

namespace ulasım_veri_servisi.Services
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
        public async Task<RouteVehiclesResponse> GetRouteVehiclesAsync(string routeNumber)
        {
            var cacheResult = await _externalEshotService.GetRouteVehiclesAsync(routeNumber);
           

            var result = new RouteVehiclesResponse
            {
                RouteNumber = routeNumber,
                RetrievedAt = DateTime.UtcNow,
                FromCache = cacheResult.FromCache
            };

            foreach (var bus in cacheResult.Data)
            {
                result.Vehicles.Add(new RouteVehicleItem
                {
                    BusId = bus.OtobusId.ToString(),
                    Direction = bus.Yon.ToString(),

                    Latitude = double.TryParse(
                        bus.KoorX?.Replace(",", "."),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var lat) ? lat : 0,

                    Longitude = double.TryParse(
                        bus.KoorY?.Replace(",", "."),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var lng) ? lng : 0
                });
            }
            return result;
        }
    }
}