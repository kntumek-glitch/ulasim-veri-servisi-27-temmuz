using System.Globalization;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;
using ulasim_veri_servisi.Helpers;
using ulasim_veri_servisi.Exceptions;

namespace ulasim_veri_servisi.Services
{
    public class ApproachingBusService
    {
        private readonly AppDbContext _context;
        private readonly IExternalEshotService _externalEshotService;

        public ApproachingBusService(
       AppDbContext context,
       IExternalEshotService externalEshotService)
        {
            _context = context;
            _externalEshotService = externalEshotService;
        }
        public async Task<ApproachingBusResponse> GetApproachingBusesAsync(int stopId)
        {
            var stop = await _context.Stops.FindAsync(stopId);
            if (stop == null)
            {
                throw new NotFoundException("Durak bulunamadı.");
            }
            var cacheResult = await _externalEshotService.GetApproachingBusesAsync(stop.ExternalStopId);
             var result = new ApproachingBusResponse
            {
                StopId = stop.Id,
                ExternalStopId = stop.ExternalStopId.ToString(),
                RetrievedAt = DateTime.UtcNow,
                 FromCache = cacheResult.FromCache,
             };
            
            foreach (var bus in cacheResult.Data)
            {
                result.Buses.Add(new ApproachingBusItem
                {
                    BusId = bus.OtobusId.ToString(),
                    RouteNumber = bus.HatNumarasi.ToString(),
                    RouteName = bus.HatAdi ?? string.Empty,
                    RemainingStopCount = bus.KalanDurakSayisi,
                    Direction = bus.HattinYonu.ToString(),
                    Latitude = CoordinateParser.ParseNullable(bus.KoorX, -90, 90),
                    Longitude = CoordinateParser.ParseNullable(bus.KoorY, -180, 180),

                    IsAccessible = bus.EngelliMi,
                    HasBicycleRack = bus.BisikletAparatliMi
                });
            }
           

         
            return result;
        }
    }
}

