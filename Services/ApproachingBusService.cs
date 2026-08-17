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
        public async Task<ApproachingBusResponse> GetApproachingBusesAsync(int stopId, CancellationToken cancellationToken = default)
        {
            var stop = await _context.Stops.FindAsync(new object[] { stopId }, cancellationToken);
            if (stop == null)
            {
                throw new NotFoundException("Durak bulunamadı.");
            }
            var cacheResult = await _externalEshotService.GetApproachingBusesAsync(stop.ExternalStopId, cancellationToken);
             var result = new ApproachingBusResponse
            {
                StopId = stop.Id,
                ExternalStopId = stop.ExternalStopId.ToString(),
                RetrievedAt = DateTime.UtcNow,
                 FromCache = cacheResult.FromCache,
             };
            
            var uniqueBuses = cacheResult.Data
                .GroupBy(b => b.OtobusId)
                .Select(g => g.Last());

            foreach (var bus in uniqueBuses)
            {
                var rawX = CoordinateParser.ParseNullable(bus.KoorX, -180, 180);
                var rawY = CoordinateParser.ParseNullable(bus.KoorY, -180, 180);
                var corrected = CoordinateParser.AutoCorrectIzmirCoordinates(rawX, rawY);

                result.Buses.Add(new ApproachingBusItem
                {
                    BusId = bus.OtobusId.ToString(),
                    RouteNumber = bus.HatNumarasi.ToString(),
                    RouteName = bus.HatAdi ?? string.Empty,
                    RemainingStopCount = bus.KalanDurakSayisi,
                    Direction = bus.HattinYonu.ToString(),
                    Latitude = corrected.Latitude,
                    Longitude = corrected.Longitude,

                    IsAccessible = bus.EngelliMi,
                    HasBicycleRack = bus.BisikletAparatliMi
                });
            }
           

         
            return result;
        }
    }
}

