using ulasim_veri_servisi.Helpers;
using ulasim_veri_servisi.Models.Gtfs;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services
{
    public class TripStopsService : ITripStopsService
    {
        private readonly ITripStopsRepository _repository;

        public TripStopsService(ITripStopsRepository repository)
        {
            _repository = repository;
        }

        public async Task<TripStopsResponseDto?> GetTripStopsAsync(string tripId, CancellationToken cancellationToken = default)
        {
            var tripData = await _repository.GetTripWithStopsFromDbAsync(tripId, cancellationToken);

            if (tripData == null)
                return null;

            // Business kuralları uygula: Zaman stringlerini saniyelere dönüştür.
            // Zaten Repository'den ASC geldiği için sıralama garantilenmiştir ancak 
            // business kuralı gereği bellekte de garanti edebiliriz.
            tripData.Stops = tripData.Stops
                .OrderBy(s => s.StopSequence)
                .Select(stop => 
                {
                    stop.ArrivalTimeSeconds = GtfsTimeHelper.ParseGtfsTimeToSeconds(stop.ArrivalTime);
                    stop.DepartureTimeSeconds = GtfsTimeHelper.ParseGtfsTimeToSeconds(stop.DepartureTime);
                    return stop;
                })
                .ToList();

            return tripData;
        }
    }
}

