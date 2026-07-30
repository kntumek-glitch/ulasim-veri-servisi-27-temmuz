using ulasim_veri_servisi.Models.Gtfs;

namespace ulasim_veri_servisi.Services.Interfaces
{
    public interface ITripStopsService
    {
        Task<TripStopsResponseDto?> GetTripStopsAsync(string tripId);
    }
}

