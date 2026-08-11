using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TransportDataService;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services.JourneyPlanning.Models;
using ulasim_veri_servisi.Services.JourneyPlanning.Spatial;

namespace ulasim_veri_servisi.Services.JourneyPlanning.DataAccess;

public class JourneyCacheService : IJourneyCacheService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ISpatialCalculatorService _spatialService;

    public JourneyCacheService(AppDbContext context, IMemoryCache cache, ISpatialCalculatorService spatialService)
    {
        _context = context;
        _cache = cache;
        _spatialService = spatialService;
    }

    public async Task<ActiveStopsCache> GetActiveStopsAsync(int activeRunId, CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync($"ActiveGtfsStops_{activeRunId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            entry.Size = 1; // Required if MemoryCache has a SizeLimit configured
            var stops = await _context.GtfsStops.Where(x => x.GtfsImportRunId == activeRunId).AsNoTracking().ToListAsync(cancellationToken);
            
            var grid = new Dictionary<string, List<GtfsStop>>();
            foreach (var s in stops)
            {
                var key = _spatialService.GetGridKey(s.StopLat, s.StopLon);
                if (!grid.TryGetValue(key, out var list))
                {
                    list = new List<GtfsStop>();
                    grid[key] = list;
                }
                list.Add(s);
            }
            var transfers = await _context.GtfsTransfers.Where(x => x.GtfsImportRunId == activeRunId).AsNoTracking().ToListAsync(cancellationToken);
            var transfersDict = transfers.GroupBy(t => t.FromStopId).ToDictionary(g => g.Key, g => g.ToList());
            var transfersToDict = transfers.ToLookup(t => t.ToStopId);
            
            return new ActiveStopsCache { Stops = stops, SpatialGrid = grid, TransfersByStopId = transfersDict, TransfersByToStopId = transfersToDict };
        }) ?? new ActiveStopsCache();
    }

    public async Task<List<string>> GetActiveServiceIdsAsync(int activeRunId, DateTime date, CancellationToken cancellationToken)
    {
        var targetDate = DateOnly.FromDateTime(date);
        var dayOfWeek = date.DayOfWeek;

        var activeCalendars = await _context.GtfsCalendars
            .AsNoTracking()
            .Where(c => c.GtfsImportRunId == activeRunId && c.StartDate <= targetDate && c.EndDate >= targetDate)
            .ToListAsync(cancellationToken);

        var validServiceIds = activeCalendars.Where(c => 
            (dayOfWeek == DayOfWeek.Monday && c.Monday) ||
            (dayOfWeek == DayOfWeek.Tuesday && c.Tuesday) ||
            (dayOfWeek == DayOfWeek.Wednesday && c.Wednesday) ||
            (dayOfWeek == DayOfWeek.Thursday && c.Thursday) ||
            (dayOfWeek == DayOfWeek.Friday && c.Friday) ||
            (dayOfWeek == DayOfWeek.Saturday && c.Saturday) ||
            (dayOfWeek == DayOfWeek.Sunday && c.Sunday)
        ).Select(c => c.ServiceId).Where(s => s != null).Cast<string>().ToList();

        // Check exceptions
        var exceptions = await _context.GtfsCalendarDates
            .AsNoTracking()
            .Where(cd => cd.GtfsImportRunId == activeRunId && cd.Date == targetDate)
            .ToListAsync(cancellationToken);

        foreach(var ex in exceptions)
        {
            if (ex.ExceptionType == 1 && !validServiceIds.Contains(ex.ServiceId)) validServiceIds.Add(ex.ServiceId);
            else if (ex.ExceptionType == 2) validServiceIds.Remove(ex.ServiceId);
        }

        return validServiceIds;
    }
}
