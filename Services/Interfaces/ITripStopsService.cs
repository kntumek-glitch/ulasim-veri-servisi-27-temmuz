using ulasım_veri_servisi.Models.Gtfs;

namespace ulasım_veri_servisi.Services.Interfaces
{
    public interface ITripStopsService
    {
        Task<TripStopsResponseDto?> GetTripStopsAsync(string tripId);
    }
}
