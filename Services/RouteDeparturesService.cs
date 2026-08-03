using Microsoft.EntityFrameworkCore;
using TransportDataService;
using ulasim_veri_servisi.Models.Gtfs;
using ulasim_veri_servisi.Services.Interfaces;

namespace ulasim_veri_servisi.Services
{
    public class RouteDeparturesService : IRouteDeparturesService
    {
        private readonly AppDbContext _context;

        public RouteDeparturesService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<RouteDeparturesResponseDto?> GetRouteDeparturesAsync(string routeId, int directionId, DateOnly date, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            // Check if the route exists
            bool routeExists = await _context.GtfsRoutes.AnyAsync(r => r.RouteId == routeId, cancellationToken);
            if (!routeExists) return null;

            // 1. Calculate Metadata
            var activeRun = await _context.GtfsImportRuns
                .Where(r => r.IsActive)
                .Select(r => new { r.FeedEndDate })
                .FirstOrDefaultAsync(cancellationToken);

            bool isFeedExpired = false;
            if (activeRun?.FeedEndDate != null)
            {
                isFeedExpired = activeRun.FeedEndDate < date;
            }

            bool missingCalendarDatesFile = !await _context.GtfsCalendarDates.AnyAsync(cancellationToken);

            // 2. Find Active ServiceIds for the given date
            var dayOfWeek = date.DayOfWeek;

            // Get base services from calendar
            var calendarServices = await _context.GtfsCalendars
                .Where(c => date >= c.StartDate && date <= c.EndDate)
                .ToListAsync(cancellationToken);

            var activeServiceIds = calendarServices
                .Where(c => IsActiveOnDay(c, dayOfWeek))
                .Select(c => c.ServiceId)
                .ToHashSet();

            // Apply exceptions from calendar_dates
            var exceptions = await _context.GtfsCalendarDates
                .Where(cd => cd.Date == date)
                .ToListAsync(cancellationToken);

            foreach (var ex in exceptions)
            {
                if (ex.ExceptionType == 1) // Added
                {
                    activeServiceIds.Add(ex.ServiceId);
                }
                else if (ex.ExceptionType == 2) // Removed
                {
                    activeServiceIds.Remove(ex.ServiceId);
                }
            }

            var calendarValidityDict = calendarServices.ToDictionary(
                c => c.ServiceId,
                c => $"{c.StartDate:yyyy-MM-dd} / {c.EndDate:yyyy-MM-dd}"
            );

            // 3. Departures Query
            var tripsQuery = _context.GtfsTrips
                .AsNoTracking()
                .Where(t => t.RouteId == routeId && t.DirectionId == directionId)
                .Where(t => activeServiceIds.Contains(t.ServiceId));

            var departuresQuery = tripsQuery
                .Select(t => new
                {
                    Trip = t,
                    FirstStop = t.StopTimes.OrderBy(st => st.StopSequence).FirstOrDefault()
                })
                .Where(x => x.FirstStop != null)
                .Select(x => new RouteDepartureDataDto
                {
                    TripId = x.Trip.TripId,
                    DirectionId = x.Trip.DirectionId,
                    Headsign = x.Trip.TripHeadsign,
                    DepartureTime = x.FirstStop!.DepartureTimeRaw,
                    DepartureSeconds = x.FirstStop!.DepartureSeconds,
                    ServiceId = x.Trip.ServiceId,
                    IsFeedStale = isFeedExpired
                });

            // Count total
            int totalRecords = 0;
            if (activeServiceIds.Any())
            {
                totalRecords = await departuresQuery.CountAsync(cancellationToken);
            }

            // Pagination
            List<RouteDepartureDataDto> data = new List<RouteDepartureDataDto>();
            if (totalRecords > 0)
            {
                data = await departuresQuery
                    .OrderBy(d => d.DepartureSeconds)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken);

                // Map CalendarValidity in memory because dictionary lookup can't be translated by EF Core
                foreach (var item in data)
                {
                    if (calendarValidityDict.TryGetValue(item.ServiceId, out var validity))
                    {
                        item.CalendarValidity = validity;
                    }
                }
            }

            return new RouteDeparturesResponseDto
            {
                Data = data,
                Pagination = new PaginationDto
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalRecords = totalRecords
                },
                Metadata = new RouteDeparturesMetadataDto
                {
                    IsFeedExpired = isFeedExpired,
                    MissingCalendarDatesFile = missingCalendarDatesFile
                }
            };
        }

        private bool IsActiveOnDay(TransportDataService.Domain.GtfsCalendar calendar, DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => calendar.Monday,
                DayOfWeek.Tuesday => calendar.Tuesday,
                DayOfWeek.Wednesday => calendar.Wednesday,
                DayOfWeek.Thursday => calendar.Thursday,
                DayOfWeek.Friday => calendar.Friday,
                DayOfWeek.Saturday => calendar.Saturday,
                DayOfWeek.Sunday => calendar.Sunday,
                _ => false
            };
        }
    }
}

