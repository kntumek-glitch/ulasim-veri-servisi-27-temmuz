using ulasım_veri_servisi.Models.Gtfs;

namespace ulasım_veri_servisi.Services.Interfaces
{
    public interface ITripStopsRepository
    {
        Task<TripStopsResponseDto?> GetTripWithStopsFromDbAsync(string tripId);
    }
}
