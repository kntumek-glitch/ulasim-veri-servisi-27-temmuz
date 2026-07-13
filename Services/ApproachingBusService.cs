using System.Globalization;
using System.Text.Json;
using TransportDataService;
using TransportDataService.Domain;

namespace ulasım_veri_servisi.Services
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
                throw new Exception("Durak bulunamadı.");
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
                    Latitude = double.TryParse(
                        bus.KoorX?.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var lat) ? lat : 0,

                    Longitude = double.TryParse(
                        bus.KoorY?.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var lng) ? lng : 0,

                    IsAccessible = bus.EngelliMi,
                    HasBicycleRack = bus.BisikletAparatliMi
                });
            }
           

         
            return result;
        }
    }
}
