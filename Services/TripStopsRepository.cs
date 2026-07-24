using Microsoft.EntityFrameworkCore;
using TransportDataService;
using ulasım_veri_servisi.Models.Gtfs;
using ulasım_veri_servisi.Services.Interfaces;

namespace ulasım_veri_servisi.Services
{
    public class TripStopsRepository : ITripStopsRepository
    {
        private readonly AppDbContext _context;

        public TripStopsRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<TripStopsResponseDto?> GetTripWithStopsFromDbAsync(string tripId)
        {
            var trip = await _context.GtfsTrips
                .Include(t => t.StopTimes)
                .ThenInclude(st => st.Stop)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null)
            {
                return null;
            }

            var dto = new TripStopsResponseDto
            {
                TripId = trip.TripId,
                RouteId = trip.RouteId,
                DirectionId = trip.DirectionId ?? 0,
                ServiceId = trip.ServiceId,
                Headsign = trip.TripHeadsign,
                ShapeId = trip.ShapeId,
                Stops = trip.StopTimes
                    .OrderBy(st => st.StopSequence)
                    .Select(st => new StopDto
                    {
                        StopId = st.Stop.StopId,
                        StopName = st.Stop.StopName,
                        StopSequence = st.StopSequence,
                        ArrivalTime = st.ArrivalTimeRaw ?? string.Empty, // using raw from db, assuming the model has it
                        DepartureTime = st.DepartureTimeRaw ?? string.Empty
                    })
                    .ToList()
            };

            return dto;
        }
    }
}
