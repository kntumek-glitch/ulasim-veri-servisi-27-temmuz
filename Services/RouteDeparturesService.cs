using Microsoft.EntityFrameworkCore;
using TransportDataService;
using ulasım_veri_servisi.Models.Gtfs;
using ulasım_veri_servisi.Services.Interfaces;

namespace ulasım_veri_servisi.Services
{
    public class RouteDeparturesService : IRouteDeparturesService
    {
        private readonly AppDbContext _context;

        public RouteDeparturesService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<RouteDeparturesResponseDto?> GetRouteDeparturesAsync(string routeId, int directionId, DateOnly date, int page, int pageSize)
        {
            // Check if the route exists
            bool routeExists = await _context.GtfsRoutes.AnyAsync(r => r.RouteId == routeId);
            if (!routeExists) return null;

            // 1. Calculate Metadata
            var activeRun = await _context.GtfsImportRuns
                .Where(r => r.IsActive)
                .Select(r => new { r.FeedEndDate })
                .FirstOrDefaultAsync();

            bool isFeedExpired = false;
            if (activeRun?.FeedEndDate != null)
            {
                isFeedExpired = activeRun.FeedEndDate < date;
            }

            bool missingCalendarDatesFile = !await _context.GtfsCalendarDates.AnyAsync();

            // 2. Find Active ServiceIds for the given date
            var dayOfWeek = date.DayOfWeek;

            // Get base services from calendar
            var calendarServices = await _context.GtfsCalendars
                .Where(c => date >= c.StartDate && date <= c.EndDate)
                .ToListAsync();

            var activeServiceIds = calendarServices
                .Where(c => IsActiveOnDay(c, dayOfWeek))
                .Select(c => c.ServiceId)
                .ToHashSet();

            // Apply exceptions from calendar_dates
            var exceptions = await _context.GtfsCalendarDates
                .Where(cd => cd.Date == date)
                .ToListAsync();

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

            // 3. Departures Query
            var departuresQuery = _context.GtfsStopTimes
                .AsNoTracking()
                .Where(st => st.Trip.RouteId == routeId && 
                             st.Trip.DirectionId == directionId && 
                             st.StopSequence == 1)
                .Where(st => activeServiceIds.Contains(st.Trip.ServiceId))
                .Select(st => new RouteDepartureDataDto
                {
                    DepartureTime = st.DepartureTimeRaw,
                    TripId = st.Trip.TripId
                });

            // Count total
            int totalRecords = 0;
            if (activeServiceIds.Any())
            {
                totalRecords = await departuresQuery.CountAsync();
            }

            // Pagination
            List<RouteDepartureDataDto> data = new List<RouteDepartureDataDto>();
            if (totalRecords > 0)
            {
                data = await departuresQuery
                    .OrderBy(d => d.DepartureTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
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
