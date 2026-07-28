using TransportDataService;
using TransportDataService.Domain;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using CsvHelper;
using System.Globalization;
using ulasım_veri_servisi.Models.Gtfs;
using CsvHelper.Configuration;
using Microsoft.Extensions.Caching.Memory;
using ulasım_veri_servisi.Exceptions;


  
namespace ulasım_veri_servisi.Services
{

    public class GtfsImportService : IGtfsImportService
    {  
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly HttpClient _httpClient;
        private readonly ILogger<GtfsImportService> _logger;
        private readonly IMemoryCache _cache;
        private const string GtfsUrl =
    "https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip";

        public GtfsImportService(
            IServiceScopeFactory scopeFactory,
            HttpClient httpClient,
            ILogger<GtfsImportService> logger,
            IMemoryCache cache)
        {
            _scopeFactory = scopeFactory;
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
        }

        public async Task<GtfsImportRun> ImportAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            bool lockAcquired = false;
            GtfsImportRun? importRun = null;
              Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
           
            string? tempFolder = null;
            try
            {
                await _context.Database.OpenConnectionAsync(cancellationToken);
                lockAcquired = await _context.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(123456) AS \"Value\"").SingleAsync(cancellationToken);
                
                if (!lockAcquired)
                {
                    throw new ConcurrentImportException("Sistemde zaten aktif olarak çalışan bir GTFS import işlemi mevcut.");
                }

                var stuckRuns = await _context.GtfsImportRuns.Where(x => x.Status == "Running").ToListAsync(cancellationToken);
                if (stuckRuns.Any())
                {
                    foreach (var stuckRun in stuckRuns)
                    {
                        stuckRun.Status = "Failed";
                        stuckRun.FinishedAt = DateTime.UtcNow;
                        stuckRun.ErrorMessage = "Automatically marked as Failed (Abandoned)";
                    }
                    await _context.SaveChangesAsync(cancellationToken);
                }

                importRun = new GtfsImportRun
                {
                    SourceUrl = GtfsUrl,
                    DownloadedAt = DateTime.UtcNow,
                    StartedAt = DateTime.UtcNow,
                    Status = "Running"
                };

                _context.GtfsImportRuns.Add(importRun);
                await _context.SaveChangesAsync(cancellationToken);

                var response = await _httpClient.GetAsync(
    GtfsUrl,
    cancellationToken);

                response.EnsureSuccessStatusCode();

                var zipBytes =
                    await response.Content.ReadAsByteArrayAsync(
                        cancellationToken);
              

                if (response.Headers.ETag != null)
                {
                    importRun.ETag =
                        response.Headers.ETag.Tag;
                }

                if (response.Content.Headers.LastModified != null)
                {
                    importRun.LastModified =
                        response.Content.Headers
                        .LastModified
                        .Value
                        .UtcDateTime;
                }
                var hash = SHA256.HashData(zipBytes);

                importRun.FileHash = Convert.ToHexString(hash);

                var alreadyImported =
        await _context.GtfsImportRuns.AnyAsync(x =>
            x.FileHash == importRun.FileHash &&
            x.Status == "Completed",
            cancellationToken);

                var hasGtfsStops =
      await _context.GtfsStops.AnyAsync(cancellationToken);

                if (alreadyImported && hasGtfsStops)
                {
                    importRun.Status = "Skipped";
                    importRun.FinishedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync(cancellationToken);

                    return importRun;
                }

                tempFolder =
    Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString());

                Directory.CreateDirectory(tempFolder);

                var zipPath =
    Path.Combine(
        tempFolder,
        "bus-eshot-gtfs.zip");

                await File.WriteAllBytesAsync(
                    zipPath,
                    zipBytes,
                    cancellationToken);

                using var archive =
                 ZipFile.OpenRead(zipPath);

                var requiredFiles = new[] { "agency.txt", "stops.txt", "routes.txt", "trips.txt", "stop_times.txt" };
                var missingFiles = requiredFiles.Where(f => archive.GetEntry(f) == null).ToList();

                var hasCalendar = archive.GetEntry("calendar.txt") != null;
                var hasCalendarDates = archive.GetEntry("calendar_dates.txt") != null;

                if (!hasCalendar && !hasCalendarDates)
                {
                    missingFiles.Add("calendar.txt veya calendar_dates.txt");
                }

                if (missingFiles.Any())
                {
                    throw new InvalidDataException($"Eksik GTFS dosyaları: {string.Join(", ", missingFiles)}");
                }

                transaction =
    await _context.Database.BeginTransactionAsync(
        cancellationToken);
                var agencyEntry = archive.GetEntry("agency.txt");
                if (agencyEntry != null)
                {
                    using var stream = agencyEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
       reader,
       CultureInfo.InvariantCulture);

                    var agencies =
                        csv.GetRecords<GtfsAgencyRow>()
                           .ToList();

                    importRun.AgencyCount = agencies.Count;

                    var agencyEntities =
    agencies.Select(x => new GtfsAgency
    {
        AgencyId = x.agency_id ?? "",
        AgencyName = x.agency_name,
        AgencyUrl = x.agency_url,
        AgencyTimezone = x.agency_timezone,
        AgencyLang = x.agency_lang,
        AgencyPhone = x.agency_phone
    }).ToList();
                    _context.GtfsAgencies.RemoveRange(
    _context.GtfsAgencies);
                    _context.GtfsAgencies.AddRange(
    agencyEntities);

                    await _context.SaveChangesAsync( 
                        cancellationToken);
                }

                var routesEntry = archive.GetEntry("routes.txt");

                if (routesEntry != null)
                {
                    using var stream = routesEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
                        reader,
                        CultureInfo.InvariantCulture);

                    var routes =
                        csv.GetRecords<GtfsRouteRow>()
                           .ToList();

                    importRun.RouteCount = routes.Count;

                    var routeEntities =
      routes.Select(x => new GtfsRoute
      {
          RouteId = x.route_id,
          AgencyId = x.agency_id ?? "",
          RouteShortName = x.route_short_name,
          RouteLongName = x.route_long_name,
          RouteDesc = x.route_desc,
          RouteType = x.route_type,
          RouteColor = string.IsNullOrWhiteSpace(x.route_color) ? null : x.route_color,
          RouteTextColor = string.IsNullOrWhiteSpace(x.route_text_color) ? null : x.route_text_color
      }).ToList();

                    _context.GtfsRoutes.RemoveRange(
                        _context.GtfsRoutes);

                    _context.GtfsRoutes.AddRange(
                        routeEntities);

                    await _context.SaveChangesAsync(
                        cancellationToken);
                }
                var stopsEntry = archive.GetEntry("stops.txt");

                if (stopsEntry != null)
                {
                    using var stream = stopsEntry.Open();

                    using var reader = new StreamReader(stream);

                    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        Delimiter = ",",
                        MissingFieldFound = null,
                        HeaderValidated = null
                    };

                    using var csv = new CsvReader(reader, config);

                    var stops =
                        csv.GetRecords<GtfsStopRow>()
                           .ToList();

                    importRun.StopCount = stops.Count;

                    var stopEntities =
     stops.Select(x => new GtfsStop
     {
         StopId = x.stop_id,
         StopCode = x.stop_code ?? string.Empty,
         StopName = x.stop_name,
         StopLat = x.stop_lat,
         StopLon = x.stop_lon,
         StopDesc = x.stop_desc,
         ZoneId = x.zone_id,
         StopUrl = x.stop_url,
         LocationType = x.location_type,
         ParentStation = x.parent_station,
         PlatformCode = x.platform_code
     }).ToList();

                    _context.GtfsStops.RemoveRange(
                        _context.GtfsStops);

                    _context.GtfsStops.AddRange(
                        stopEntities);

                    await _context.SaveChangesAsync(
                        cancellationToken);
                   
                }


                var tripsEntry = archive.GetEntry("trips.txt");

                if (tripsEntry != null)
                {
                    using var stream = tripsEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
                        reader,
                        CultureInfo.InvariantCulture);

                    var trips =
                        csv.GetRecords<GtfsTripRow>()
                           .ToList();

                    importRun.TripCount = trips.Count;
                   

                    var routeLookup = await _context.GtfsRoutes
    .ToDictionaryAsync(
        x => x.RouteId,
        x => x.Id,
        cancellationToken);

                    var tripEntities =
    trips
    .Where(x => routeLookup.ContainsKey(x.route_id))
    .Select(x => new GtfsTrip
    {
        TripId = x.trip_id,
        RouteId = x.route_id,
        GtfsRouteId = routeLookup[x.route_id],
        ServiceId = x.service_id,
        DirectionId = x.direction_id,
        ShapeId = x.shape_id,
        TripHeadsign = x.trip_headsign
    })
    .ToList();
                    
                    _context.GtfsTrips.RemoveRange(_context.GtfsTrips);
                   
                    await _context.SaveChangesAsync(cancellationToken);

                    const int batchSize = 500;

                    for (int i = 0; i < tripEntities.Count; i += batchSize)
                    {
                        var batch = tripEntities
                            .Skip(i)
                            .Take(batchSize)
                            .ToList();

                        _context.GtfsTrips.AddRange(batch);

                        await _context.SaveChangesAsync(cancellationToken);
                      

                        _context.ChangeTracker.Clear();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
                var stopTimesEntry = archive.GetEntry("stop_times.txt");

                if (stopTimesEntry != null)
                {
                    using var stream = stopTimesEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
                        reader,
                        CultureInfo.InvariantCulture);

                    var stopTimes = csv.GetRecords<GtfsStopTimeRow>();
                    var stopLookup = await _context.GtfsStops
    .ToDictionaryAsync(
        x => x.StopId,
        x => x.Id,
        cancellationToken);

                    var tripLookup = await _context.GtfsTrips
                        .ToDictionaryAsync(
                            x => x.TripId,
                            x => x.Id,
                            cancellationToken);
                    _context.GtfsStopTimes.RemoveRange(_context.GtfsStopTimes);
                    await _context.SaveChangesAsync(cancellationToken);

                    const int batchSize = 500;

                    var batch = new List<GtfsStopTime>(batchSize);

                    int total = 0;

                    foreach (var x in stopTimes)
                    {
                        if (!stopLookup.TryGetValue(x.stop_id, out var stopDbId))
                            continue;

                        if (!tripLookup.TryGetValue(x.trip_id, out var tripDbId))
                            continue;

                        batch.Add(new GtfsStopTime
                        {
                            TripId = x.trip_id,
                            StopId = x.stop_id,

                            GtfsTripId = tripDbId,
                            GtfsStopId = stopDbId,

                            ArrivalTimeRaw = x.arrival_time,
                            DepartureTimeRaw = x.departure_time,

                            ArrivalSeconds = GtfsTimeParser.ParseToSeconds(x.arrival_time),
                            DepartureSeconds = GtfsTimeParser.ParseToSeconds(x.departure_time),

                            StopSequence = x.stop_sequence
                        });

                        total++;

                        if (batch.Count >= batchSize)
                        {
                            _context.GtfsStopTimes.AddRange(batch);

                            await _context.SaveChangesAsync(cancellationToken);

                            _context.ChangeTracker.Clear();
                            GC.Collect();
                            GC.WaitForPendingFinalizers();

                            batch.Clear();
                        }
                    }

                    if (batch.Count > 0)
                    {
                        _context.GtfsStopTimes.AddRange(batch);

                        await _context.SaveChangesAsync(cancellationToken);

                        _context.ChangeTracker.Clear();
                    }

                    importRun.StopTimeCount = total;


                }
                var calendarEntry = archive.GetEntry("calendar.txt");

                if (calendarEntry != null)
                {
                    using var stream = calendarEntry.Open();

                    using var reader = new StreamReader(stream);
                    using var csv =
    new CsvReader(
        reader,
        CultureInfo.InvariantCulture);
                    var calendars =
    csv.GetRecords<GtfsCalendarRow>()
       .ToList();
                    var calendarEntities =
    calendars.Select(x => new GtfsCalendar
    {
        ServiceId = x.service_id,
        Monday = x.monday == 1,
        Tuesday = x.tuesday == 1,

        Wednesday = x.wednesday == 1,

        Thursday = x.thursday == 1,

        Friday = x.friday == 1,

        Saturday = x.saturday == 1,

        Sunday = x.sunday == 1,
        StartDate =
    DateOnly.ParseExact(
        x.start_date,
        "yyyyMMdd"),
        EndDate =
    DateOnly.ParseExact(
        x.end_date,
        "yyyyMMdd"),
    }).ToList();

                    _context.GtfsCalendars.RemoveRange(
      _context.GtfsCalendars);

                    _context.GtfsCalendars.AddRange(
                        calendarEntities);

                    await _context.SaveChangesAsync(
                        cancellationToken);

                }

                var calendarDatesEntry = archive.GetEntry("calendar_dates.txt");

                if (calendarDatesEntry != null)
                {
                    using var stream = calendarDatesEntry.Open();

                    using var reader = new StreamReader(stream);
                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                    
                    var calendarDates = csv.GetRecords<GtfsCalendarDateRow>().ToList();
                    
                    var calendarDateEntities = calendarDates.Select(x => new GtfsCalendarDate
                    {
                        ServiceId = x.service_id,
                        Date = DateOnly.ParseExact(x.date, "yyyyMMdd"),
                        ExceptionType = x.exception_type
                    }).ToList();

                    _context.GtfsCalendarDates.RemoveRange(_context.GtfsCalendarDates);
                    _context.GtfsCalendarDates.AddRange(calendarDateEntities);

                    await _context.SaveChangesAsync(cancellationToken);
                }

                var shapesEntry = archive.GetEntry("shapes.txt");

                if (shapesEntry != null)
                {
                    using var stream = shapesEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
                        reader,
                        CultureInfo.InvariantCulture);

                    var shapes = csv.GetRecords<GtfsShapePointRow>();

              

                    _context.GtfsShapePoints.RemoveRange(_context.GtfsShapePoints);

                    await _context.SaveChangesAsync(cancellationToken);

                    const int batchSize = 500;

                    var batch = new List<GtfsShapePoint>(batchSize);

                    int total = 0;

                    foreach (var x in shapes)
                    {
                        batch.Add(new GtfsShapePoint
                        {
                            ShapeId = x.shape_id,
                            Latitude = x.shape_pt_lat,
                            Longitude = x.shape_pt_lon,
                            Sequence = x.shape_pt_sequence
                        });

                        total++;

                        if (batch.Count >= batchSize)
                        {
                            _context.GtfsShapePoints.AddRange(batch);

                            await _context.SaveChangesAsync(cancellationToken);

                             _context.ChangeTracker.Clear();

                            batch.Clear();
                        }
                    }
                    importRun.ShapePointCount = total;
                    _context.GtfsImportRuns.Update(importRun);

                    await _context.SaveChangesAsync(cancellationToken);

                    if (batch.Count > 0)
                    {
                        _context.GtfsShapePoints.AddRange(batch);

                        await _context.SaveChangesAsync(cancellationToken);

                        _context.ChangeTracker.Clear();
                    }

                    importRun.ShapePointCount = total;
                }
              
                var feedInfoEntry = archive.GetEntry("feed_info.txt");

                if (feedInfoEntry != null)
                {
                    using var stream = feedInfoEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
                        reader,
                        CultureInfo.InvariantCulture);

                    var feedInfo =
                        csv.GetRecords<GtfsFeedInfoRow>()
                           .FirstOrDefault();

                    if (feedInfo != null)
                    {
                        importRun.FeedVersion =
                            feedInfo.feed_version;

                        if (DateOnly.TryParseExact(
                            feedInfo.feed_start_date,
                            "yyyyMMdd",
                            out var startDate))
                        {
                            importRun.FeedStartDate = startDate;
                        }

                        if (DateOnly.TryParseExact(
                            feedInfo.feed_end_date,
                            "yyyyMMdd",
                            out var endDate))
                        {
                            importRun.FeedEndDate = endDate;
                        }
                    }
                }

                if (importRun.FeedStartDate == null || importRun.FeedEndDate == null)
                {
                    var calendarMinDate = await _context.GtfsCalendars.MinAsync(c => (DateOnly?)c.StartDate, cancellationToken);
                    var calendarMaxDate = await _context.GtfsCalendars.MaxAsync(c => (DateOnly?)c.EndDate, cancellationToken);
                    
                    var exceptionMinDate = await _context.GtfsCalendarDates.MinAsync(c => (DateOnly?)c.Date, cancellationToken);
                    var exceptionMaxDate = await _context.GtfsCalendarDates.MaxAsync(c => (DateOnly?)c.Date, cancellationToken);

                    var minDates = new List<DateOnly>();
                    if (calendarMinDate.HasValue) minDates.Add(calendarMinDate.Value);
                    if (exceptionMinDate.HasValue) minDates.Add(exceptionMinDate.Value);

                    var maxDates = new List<DateOnly>();
                    if (calendarMaxDate.HasValue) maxDates.Add(calendarMaxDate.Value);
                    if (exceptionMaxDate.HasValue) maxDates.Add(exceptionMaxDate.Value);

                    if (minDates.Any())
                        importRun.FeedStartDate = minDates.Min();
                    
                    if (maxDates.Any())
                        importRun.FeedEndDate = maxDates.Max();

                    if (string.IsNullOrWhiteSpace(importRun.FeedVersion))
                    {
                        importRun.FeedVersion = "unavailable (derived from calendar)";
                    }
                }
                importRun.Status = "Completed";
                importRun.FinishedAt = DateTime.UtcNow;
                importRun.IsActive = true;

                // Agresif olarak eski tüm önbellek kalıntılarını siler.
                if (_cache is MemoryCache memoryCache)
                {
                    memoryCache.Clear();
                }

                // The feed data and its active marker are committed as one unit.
                // A failed import rolls this transaction back, leaving the prior
                // active feed available to all read endpoints.
                await _context.GtfsImportRuns
                    .Where(x => x.IsActive)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.IsActive, false),
                        cancellationToken);

                _context.GtfsImportRuns.Update(importRun);

                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                archive.Dispose();

                if (tempFolder != null && Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }

                return importRun;
            }
            catch (Exception ex)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                if (importRun != null)
                {
                    if (tempFolder != null && Directory.Exists(tempFolder))
                    {
                        Directory.Delete(tempFolder, true);
                    }

                    // The context can still track inserts/deletes from the rolled
                    // back transaction. Clear it before writing the history row so
                    // a failed import cannot accidentally persist partial feed data.
                    _context.ChangeTracker.Clear();

                    var failedRun = await _context.GtfsImportRuns
                        .SingleAsync(x => x.Id == importRun.Id, CancellationToken.None);
                    failedRun.Status = "Failed";
                    failedRun.ErrorMessage = "İçe aktarım sırasında beklenmeyen bir hata oluştu. Lütfen sistem loglarını kontrol edin.";
                    _logger.LogError(ex, "GTFS import failed for run {RunId}", importRun.Id);
                    failedRun.FinishedAt = DateTime.UtcNow;
                    failedRun.IsActive = false;

                    await _context.SaveChangesAsync(CancellationToken.None);
                }

                throw;
            }
            finally
            {
                if (lockAcquired)
                {
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(123456)");
                }
                await _context.Database.CloseConnectionAsync();
            }
          
        }

    }
}
