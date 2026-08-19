using TransportDataService;
using TransportDataService.Domain;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using CsvHelper;
using System.Globalization;
using ulasim_veri_servisi.Models.Gtfs;
using CsvHelper.Configuration;
using Microsoft.Extensions.Caching.Memory;
using ulasim_veri_servisi.Exceptions;
using Npgsql;
using ulasim_veri_servisi.Services.Interfaces;

  
namespace ulasim_veri_servisi.Services
{

    public class GtfsImportService : IGtfsImportService
    {  
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly HttpClient _httpClient;
        private readonly ILogger<GtfsImportService> _logger;
        private readonly IMemoryCache _cache;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private readonly IRoutingSnapshotManager _snapshotManager;
        

        public GtfsImportService(
            IServiceScopeFactory scopeFactory,
            HttpClient httpClient,
            ILogger<GtfsImportService> logger,
            IMemoryCache cache,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IRoutingSnapshotManager snapshotManager)
        {
            _scopeFactory = scopeFactory;
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _configuration = configuration;
            _snapshotManager = snapshotManager;
        }

        public async Task<GtfsImportRun> ImportAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            _context.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            bool lockAcquired = false;
            GtfsImportRun? importRun = null;
            GtfsImportPhase? activePhase = null;
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
                    SourceUrl = "Multi-source",
                    DownloadedAt = DateTime.UtcNow,
                    StartedAt = DateTime.UtcNow,
                    Status = "Running"
                };

                _context.GtfsImportRuns.Add(importRun);
                await _context.SaveChangesAsync(cancellationToken);

                
                var sources = _configuration.GetSection("GtfsSources").Get<List<TransportDataService.Domain.Configuration.GtfsSourceConfig>>() 
                              ?? new List<TransportDataService.Domain.Configuration.GtfsSourceConfig> { new() { Prefix = "ESHOT", Url = "https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip" } };

                tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempFolder);

                activePhase = await StartPhaseAsync(_context, importRun.Id, "Downloading", cancellationToken);

                var sourcePaths = new Dictionary<string, string>();
                string combinedHash = "";

                foreach (var source in sources)
                {
                    try {
                        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
                        var response = await _httpClient.SendAsync(request, cancellationToken);
                        response.EnsureSuccessStatusCode();

                        var zipBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        var hashBytes = System.Security.Cryptography.SHA256.HashData(zipBytes);
                        combinedHash += Convert.ToHexString(hashBytes) + "_";

                        var zipPath = Path.Combine(tempFolder, $"{source.Prefix}.zip");
                        await System.IO.File.WriteAllBytesAsync(zipPath, zipBytes, cancellationToken);
                        sourcePaths[source.Prefix] = zipPath;
                    } catch (Exception ex) {
                        _logger.LogWarning($"Failed to download source {source.Prefix}: {ex.Message}");
                    }
                }

                importRun.FileHash = combinedHash.TrimEnd('_');

                if (activePhase != null)
                {
                    await CompletePhaseAsync(_context, activePhase, cancellationToken);
                }

                importRun.IsActive = false; // Staging

                activePhase = await StartPhaseAsync(_context, importRun.Id, "Parsing & Importing", cancellationToken);

                foreach (var kvp in sourcePaths)
                {
                    string prefix = kvp.Key;
                    string zipPath = kvp.Value;
                    
                    using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
                    
var agencyEntry = archive.GetEntry("agency.txt");
                if (agencyEntry != null)
                {
                    using var stream = agencyEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
       reader,
                        new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });

                    var agencies =
                        csv.GetRecords<GtfsAgencyRow>()
                           .ToList();

                    importRun.AgencyCount += agencies.Count;

                    var agencyEntities =
    agencies.Select(x => new GtfsAgency
    {
        GtfsImportRunId = importRun.Id,
        AgencyId = prefix + "_" + (x.agency_id ?? ""),
        AgencyName = x.agency_name,
        AgencyUrl = x.agency_url,
        AgencyTimezone = x.agency_timezone,
        AgencyLang = x.agency_lang,
        AgencyPhone = x.agency_phone
    }).ToList();

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
                        new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });

                    var routes =
                        csv.GetRecords<GtfsRouteRow>()
                           .Where(r => (r.agency_id ?? "").ToUpper() != "IZBAN")
                           .ToList();

                    importRun.RouteCount += routes.Count;

                    var routeEntities =
      routes.Select(x => new GtfsRoute
      {
          GtfsImportRunId = importRun.Id,
          RouteId = prefix + "_" + x.route_id,
          AgencyId = prefix + "_" + (x.agency_id ?? ""),
          RouteShortName = x.route_short_name,
          RouteLongName = x.route_long_name,
          RouteDesc = x.route_desc,
          RouteType = x.route_type,
          RouteColor = string.IsNullOrWhiteSpace(x.route_color) ? null : x.route_color,
          RouteTextColor = string.IsNullOrWhiteSpace(x.route_text_color) ? null : x.route_text_color
      }).ToList();



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

                    importRun.StopCount += stops.Count;

                    foreach (var s in stops)
                    {
                        if (s.stop_lat < -90 || s.stop_lat > 90 || s.stop_lon < -180 || s.stop_lon > 180)
                        {
                            throw new InvalidGtfsFeedException($"Geçersiz durak koordinatları tespit edildi. Durak ID: {s.stop_id} (Lat: {s.stop_lat}, Lon: {s.stop_lon})");
                        }
                    }

                    var stopEntities =
     stops.Select(x => new GtfsStop
     {
         GtfsImportRunId = importRun.Id,
         StopId = prefix + "_" + x.stop_id,
         StopCode = x.stop_code ?? string.Empty,
         StopName = x.stop_name,
         StopLat = x.stop_lat,
         StopLon = x.stop_lon,
         StopDesc = x.stop_desc,
         ZoneId = x.zone_id,
         StopUrl = x.stop_url,
         LocationType = x.location_type,
         ParentStation = string.IsNullOrEmpty(x.parent_station) ? null : prefix + "_" + x.parent_station,
         PlatformCode = x.platform_code
     }).ToList();



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
                        new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });

                    var trips =
                        csv.GetRecords<GtfsTripRow>()
                           .ToList();

                    importRun.TripCount += trips.Count;
                   

                    var routeLookup = await _context.GtfsRoutes
                        .IgnoreQueryFilters()
                        .Where(x => x.GtfsImportRunId == importRun.Id)
                        .ToDictionaryAsync(
                            x => x.RouteId,
                            x => x.Id,
                            cancellationToken);

                    var tripEntities =
    trips
    .Where(x => routeLookup.ContainsKey(prefix + "_" + x.route_id))
    .Select(x => new GtfsTrip
    {
        GtfsImportRunId = importRun.Id,
        TripId = prefix + "_" + x.trip_id,
        RouteId = prefix + "_" + x.route_id,
        GtfsRouteId = routeLookup[prefix + "_" + x.route_id],
        ServiceId = prefix + "_" + x.service_id,
        DirectionId = x.direction_id,
        ShapeId = string.IsNullOrEmpty(x.shape_id) ? null : prefix + "_" + x.shape_id,
        TripHeadsign = x.trip_headsign
    })
    .ToList();
                    

                   
                    await _context.SaveChangesAsync(cancellationToken);

                    int batchSize = _configuration.GetValue<int>("GtfsImport:BatchSize", 10000);

                    for (int i = 0; i < tripEntities.Count; i += batchSize)
                    {
                        var batch = tripEntities
                            .Skip(i)
                            .Take(batchSize)
                            .ToList();

                        _context.GtfsTrips.AddRange(batch);

                        await _context.SaveChangesAsync(cancellationToken);
                      

                        _context.ChangeTracker.Clear();
                    }
                }
                var stopTimesEntry = archive.GetEntry("stop_times.txt");

                if (stopTimesEntry != null)
                {
                    using var stream = stopTimesEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
                        reader,
                        new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });

                    var stopTimes = csv.GetRecords<GtfsStopTimeRow>();
                    var stopLookup = await _context.GtfsStops
                        .IgnoreQueryFilters()
                        .Where(x => x.GtfsImportRunId == importRun.Id)
                        .ToDictionaryAsync(
                            x => x.StopId,
                            x => x.Id,
                            cancellationToken);

                    var tripLookup = await _context.GtfsTrips
                        .IgnoreQueryFilters()
                        .Where(x => x.GtfsImportRunId == importRun.Id)
                        .ToDictionaryAsync(
                            x => x.TripId,
                            x => x.Id,
                            cancellationToken);

                    await _context.SaveChangesAsync(cancellationToken);

                    int batchSize = _configuration.GetValue<int>("GtfsImport:BatchSize", 10000);

                    var batch = new List<GtfsStopTime>(batchSize);

                    int total = 0;

                    foreach (var x in stopTimes)
                    {
                        if (!stopLookup.TryGetValue(prefix + "_" + x.stop_id, out var stopDbId))
                            continue;

                        if (!tripLookup.TryGetValue(prefix + "_" + x.trip_id, out var tripDbId))
                            continue;

                        batch.Add(new GtfsStopTime
                        {
                            GtfsImportRunId = importRun.Id,
                            TripId = prefix + "_" + x.trip_id,
                            StopId = prefix + "_" + x.stop_id,

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

                            batch.Clear();
                            
                            // Update progress occasionally
                            await UpdatePhaseAsync(_context, activePhase, 0, total, cancellationToken);
                        }
                    }

                    if (batch.Count > 0)
                    {
                        _context.GtfsStopTimes.AddRange(batch);

                        await _context.SaveChangesAsync(cancellationToken);

                        _context.ChangeTracker.Clear();
                    }

                    importRun.StopTimeCount += total;

                }
                var calendarEntry = archive.GetEntry("calendar.txt");

                if (calendarEntry != null)
                {
                    using var stream = calendarEntry.Open();

                    using var reader = new StreamReader(stream);
                    using var csv =
    new CsvReader(
        reader,
        new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });
                    var calendars =
    csv.GetRecords<GtfsCalendarRow>()
       .ToList();
                    var calendarEntities =
    calendars.Select(x => new GtfsCalendar
    {
        GtfsImportRunId = importRun.Id,
        ServiceId = prefix + "_" + x.service_id,
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
                    using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });
                    
                    var calendarDates = csv.GetRecords<GtfsCalendarDateRow>().ToList();
                    
                    var calendarDateEntities = calendarDates.Select(x => new GtfsCalendarDate
                    {
                        GtfsImportRunId = importRun.Id,
                        ServiceId = prefix + "_" + x.service_id,
                        Date = DateOnly.ParseExact(x.date, "yyyyMMdd"),
                        ExceptionType = x.exception_type
                    }).ToList();


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
                        new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });

                    var shapes = csv.GetRecords<GtfsShapePointRow>();

              



                    await _context.SaveChangesAsync(cancellationToken);

                    int batchSize = _configuration.GetValue<int>("GtfsImport:BatchSize", 10000);

                    var batch = new List<GtfsShapePoint>(batchSize);

                    int total = 0;

                    foreach (var x in shapes)
                    {
                        if (x.shape_pt_lat < -90 || x.shape_pt_lat > 90 || x.shape_pt_lon < -180 || x.shape_pt_lon > 180)
                        {
                            throw new InvalidGtfsFeedException($"Geçersiz rota (shape) koordinatları tespit edildi. Shape ID: {x.shape_id}");
                        }

                        batch.Add(new GtfsShapePoint
                        {
                            GtfsImportRunId = importRun.Id,
                            ShapeId = string.IsNullOrEmpty(x.shape_id) ? null : prefix + "_" + x.shape_id,
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
                            
                            // Update progress occasionally
                            await UpdatePhaseAsync(_context, activePhase, 0, total, cancellationToken);
                        }
                    }
                    importRun.ShapePointCount += total;
                    _context.GtfsImportRuns.Update(importRun);

                    await _context.SaveChangesAsync(cancellationToken);

                    if (batch.Count > 0)
                    {
                        _context.GtfsShapePoints.AddRange(batch);

                        await _context.SaveChangesAsync(cancellationToken);

                        _context.ChangeTracker.Clear();
                    }

                    importRun.ShapePointCount += total;
                }
              
                var feedInfoEntry = archive.GetEntry("feed_info.txt");

                if (feedInfoEntry != null)
                {
                    using var stream = feedInfoEntry.Open();

                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(
                        reader,
                        new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null });

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
                    var calendarMinDate = await _context.GtfsCalendars.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRun.Id).MinAsync(c => (DateOnly?)c.StartDate, cancellationToken);
                    var calendarMaxDate = await _context.GtfsCalendars.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRun.Id).MaxAsync(c => (DateOnly?)c.EndDate, cancellationToken);
                    
                    var exceptionMinDate = await _context.GtfsCalendarDates.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRun.Id).MinAsync(c => (DateOnly?)c.Date, cancellationToken);
                    var exceptionMaxDate = await _context.GtfsCalendarDates.IgnoreQueryFilters().Where(x => x.GtfsImportRunId == importRun.Id).MaxAsync(c => (DateOnly?)c.Date, cancellationToken);

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

                if (importRun.FeedStartDate.HasValue && importRun.FeedEndDate.HasValue)
                {
                    if (importRun.FeedEndDate.Value < importRun.FeedStartDate.Value)
                    {
                        throw new InvalidGtfsFeedException($"Geçersiz feed tarihleri: Bitiş tarihi ({importRun.FeedEndDate.Value}) Başlangıç tarihinden ({importRun.FeedStartDate.Value}) önce olamaz.");
                    }
                }
                
                _logger.LogInformation($"[{prefix}] GTFS verileri başarıyla aktarıldı.");
                } // End of foreach sourcePaths

                _logger.LogInformation("Generating GtfsTripStopSummaries...");
                await _context.Database.ExecuteSqlRawAsync($@"
                    INSERT INTO ""GtfsTripStopSummaries"" (""GtfsImportRunId"", ""GtfsTripId"", ""StopSequences"")
                    SELECT ""GtfsImportRunId"", ""GtfsTripId"", array_agg(""StopSequence"" ORDER BY ""StopSequence"")
                    FROM ""GtfsStopTimes""
                    WHERE ""GtfsImportRunId"" = {0}
                    GROUP BY ""GtfsImportRunId"", ""GtfsTripId"";
                ", importRun.Id);
                _logger.LogInformation("GtfsTripStopSummaries generated successfully.");

                await CompletePhaseAsync(_context, activePhase, cancellationToken);
                activePhase = await StartPhaseAsync(_context, importRun.Id, "Validating", cancellationToken);

                if (importRun.StopCount < 10) throw new InvalidGtfsFeedException($"Durak sayısı çok az ({importRun.StopCount}). Minimum 10 durak gereklidir.");
                if (importRun.RouteCount < 1) throw new InvalidGtfsFeedException($"Rota bulunamadı. Minimum 1 rota gereklidir.");
                if (importRun.TripCount < 10) throw new InvalidGtfsFeedException($"Trip sayısı çok az ({importRun.TripCount}). Minimum 10 trip gereklidir.");
                if (importRun.StopTimeCount < 100) throw new InvalidGtfsFeedException($"StopTime sayısı çok az ({importRun.StopTimeCount}). Minimum 100 stop_time gereklidir.");

                importRun.Status = "Completed";
                importRun.FinishedAt = DateTime.UtcNow;
                
                await CompletePhaseAsync(_context, activePhase, cancellationToken);
                
                activePhase = await StartPhaseAsync(_context, importRun.Id, "CalculatingTransfers", cancellationToken);
                var transferCalcService = scope.ServiceProvider.GetRequiredService<IGtfsTransferCalculationService>();
                await transferCalcService.CalculateTransfersAsync(importRun.Id, cancellationToken);
                await CompletePhaseAsync(_context, activePhase, cancellationToken);
                
                activePhase = await StartPhaseAsync(_context, importRun.Id, "Building Snapshot", cancellationToken);
                var candidateSnapshot = await _snapshotManager.BuildCandidateSnapshotAsync(importRun.Id, importRun.FileHash, cancellationToken);
                await CompletePhaseAsync(_context, activePhase, cancellationToken);

                activePhase = await StartPhaseAsync(_context, importRun.Id, "Activating", cancellationToken);

                // Agresif olarak eski tüm önbellek kalıntılarını siler.
                if (_cache is MemoryCache memoryCache)
                {
                    memoryCache.Clear();
                }

                // Sadece AKTİVASYON için kısa ömürlü ve atomik bir transaction açıyoruz.
                await using var swapTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                // Eski aktif feed'i pasife çekiyoruz
                await _context.GtfsImportRuns
                    .Where(x => x.IsActive)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.IsActive, false),
                        cancellationToken);

                importRun.IsActive = true;
                _context.GtfsImportRuns.Update(importRun);

                await _context.SaveChangesAsync(cancellationToken);

                // Aktivasyonu atomik olarak commitliyoruz. Artık canlı API'ler bu yeni veriyi kullanacak.
                await swapTransaction.CommitAsync(cancellationToken);
                
                // DB Commit başarılıysa Snapshot referansını da atomik olarak güncelliyoruz (Promote)
                _snapshotManager.PromoteSnapshot(candidateSnapshot);
                
                await CompletePhaseAsync(_context, activePhase, cancellationToken);
                activePhase = null;

                // Temizlik işlemi artık ayrı bir metotta (CleanupOldFeedsAsync) yapılacak.
                // Temizlik işlemi artık ayrı bir metotta (CleanupOldFeedsAsync) yapılacak.
                // Bu sayede import süreci kilitlenmeden hızlıca dönebilecek.                if (tempFolder != null && Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }

                // Arka planda ateşle ve unut (Fire-and-Forget) mantığıyla temizliği tetikliyoruz.
                // Kendi scope'unu oluşturması için Task.Run içerisine alıyoruz ki mevcut Request scope kapanmasın.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using var cleanupScope = _scopeFactory.CreateAsyncScope();
                        var service = cleanupScope.ServiceProvider.GetRequiredService<IGtfsImportService>();
                        await service.CleanupOldFeedsAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background cleanup task failed.");
                    }
                });

                
                var duration = (importRun.FinishedAt.Value - importRun.StartedAt).TotalSeconds;
                _logger.LogInformation("GTFS Import completed successfully in {DurationSeconds:F2}s. RunId: {RunId}, Hash: {FileHash}, Agencies: {AgencyCount}, Routes: {RouteCount}, Stops: {StopCount}, Trips: {TripCount}, StopTimes: {StopTimeCount}, ShapePoints: {ShapePointCount}", 
                    duration, importRun.Id, importRun.FileHash, importRun.AgencyCount, importRun.RouteCount, importRun.StopCount, importRun.TripCount, importRun.StopTimeCount, importRun.ShapePointCount);

                return importRun;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GTFS Import failed. RunId: {RunId}, Hash: {FileHash}", importRun?.Id, importRun?.FileHash);
                if (transaction != null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    await transaction.DisposeAsync();
                }

                if (importRun != null)
                {
                    if (tempFolder != null && Directory.Exists(tempFolder))
                    {
                        Directory.Delete(tempFolder, true);
                    }

                    // Ayrı bir scope açarak rollback'ten etkilenmeden log yazıyoruz
                    await using var errorScope = _scopeFactory.CreateAsyncScope();
                    var errorContext = errorScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    if (activePhase != null)
                    {
                        var phaseToFail = await errorContext.GtfsImportPhases.SingleOrDefaultAsync(p => p.Id == activePhase.Id);
                        if (phaseToFail != null)
                        {
                            phaseToFail.FinishedAt = DateTime.UtcNow;
                            phaseToFail.ErrorMessage = ex.GetType().Name + ": " + ex.Message;
                            errorContext.GtfsImportPhases.Update(phaseToFail);
                        }
                    }

                    // Hatalı yüklenen pasif (staging) verileri kirlilik yapmaması için siliyoruz
                    await errorContext.GtfsTransfers.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsStopTimes.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsTrips.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsRoutes.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsShapePoints.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsCalendarDates.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsCalendars.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsStops.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);
                    await errorContext.GtfsAgencies.Where(x => x.GtfsImportRunId == importRun.Id).ExecuteDeleteAsync(CancellationToken.None);

                    var failedRun = await errorContext.GtfsImportRuns
                        .FirstOrDefaultAsync(x => x.Id == importRun.Id, CancellationToken.None);
                    
                    if (failedRun == null)
                    {
                        _logger.LogCritical("CRITICAL BUG: importRun (Id={Id}) NOT FOUND in errorContext! Transaction was {Tx}", importRun.Id, transaction != null ? "Started" : "Not Started");
                        // We must recreate it so the status can be updated
                        failedRun = importRun;
                        errorContext.GtfsImportRuns.Attach(failedRun);
                    }
                    if (ex is OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            failedRun.Status = "Cancelled";
                            failedRun.ErrorMessage = "İşlem kullanıcı veya sistem tarafından iptal edildi.";
                        }
                        else
                        {
                            failedRun.Status = "Failed";
                            failedRun.ErrorMessage = "Dış kaynak zaman aşımına uğradı.";
                        }
                    }
                    else
                    {
                        failedRun.Status = "Failed";
                        if (ex is Exceptions.InvalidGtfsFeedException || ex is InvalidDataException)
                        {
                            failedRun.ErrorMessage = ex.Message;
                        }
                        else
                        {
                            failedRun.ErrorMessage = "İçe aktarım sırasında beklenmeyen bir hata oluştu. Lütfen sistem loglarını kontrol edin.";
                        }
                    }

                    failedRun.FinishedAt = DateTime.UtcNow;
                    failedRun.IsActive = false;

                    await errorContext.SaveChangesAsync(CancellationToken.None);

                    if (failedRun.Status == "Cancelled")
                        _logger.LogWarning("GTFS import cancelled for run {RunId}", importRun.Id);
                    else
                        _logger.LogError(ex, "GTFS import failed for run {RunId}", importRun.Id);
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

        public async Task CleanupOldFeedsAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _logger.LogInformation("Starting background cleanup of old GTFS feeds...");

            int keepCompletedCount = _configuration.GetValue<int>("GtfsImport:KeepCompletedCount", 2);
            int retentionDays = _configuration.GetValue<int>("GtfsImport:RetentionDays", 7);
            var retentionCutoff = DateTime.UtcNow.AddDays(-retentionDays);

            // Kural: Son başarılı (Completed) feed'leri tut
            var completedRuns = await context.GtfsImportRuns
                .Where(x => x.Status == "Completed" && !x.IsActive)
                .OrderByDescending(x => x.Id)
                .Select(x => x.Id)
                .Take(keepCompletedCount)
                .ToListAsync(cancellationToken);

            // Aktif olanı mutlaka tut
            var activeRunId = await context.GtfsImportRuns
                .Where(x => x.IsActive)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // "Running" statüsünde olanları tut
            var runningRuns = await context.GtfsImportRuns
                .Where(x => x.Status == "Running")
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            // Belirli bir günden daha yeni olan Failed, Skipped, Cancelled import geçmişlerini koru
            var recentFailedRuns = await context.GtfsImportRuns
                .Where(x => (x.Status == "Failed" || x.Status == "Cancelled" || x.Status == "Skipped") && x.FinishedAt >= retentionCutoff)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var runsToKeep = new HashSet<int>(completedRuns);
            if (activeRunId > 0) runsToKeep.Add(activeRunId);
            foreach (var id in runningRuns) runsToKeep.Add(id);
            foreach (var id in recentFailedRuns) runsToKeep.Add(id);

            var keepList = runsToKeep.ToList();

            var runsToDelete = await context.GtfsImportRuns
                .Where(x => !keepList.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            if (!runsToDelete.Any())
            {
                _logger.LogInformation("No old GTFS feeds to clean up.");
                return;
            }

            _logger.LogInformation("Cleaning up {Count} old GTFS feeds (IDs: {Ids})", runsToDelete.Count, string.Join(",", runsToDelete));

            await context.GtfsTransfers.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsTripStopSummaries.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsStopTimes.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsTrips.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsRoutes.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsShapePoints.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsCalendarDates.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsCalendars.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsStops.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            await context.GtfsAgencies.Where(x => runsToDelete.Contains(x.GtfsImportRunId)).ExecuteDeleteAsync(cancellationToken);
            
            var deletedPhasesCount = await context.GtfsImportPhases
                .Where(x => runsToDelete.Contains(x.GtfsImportRunId))
                .ExecuteDeleteAsync(cancellationToken);

            var deletedRunsCount = await context.GtfsImportRuns
                .Where(x => runsToDelete.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);

            var metrics = new
            {
                DeletedRuns = deletedRunsCount,
                DeletedPhases = deletedPhasesCount,
                KeptCompleted = completedRuns.Count,
                KeptRecentFailed = recentFailedRuns.Count,
                KeptRunning = runningRuns.Count,
                RetentionDays = retentionDays
            };

            _logger.LogInformation("Retention cleanup completed. Metrics: {@Metrics}", metrics);
        }

        private async Task<GtfsImportPhase> StartPhaseAsync(AppDbContext context, int runId, string phaseName, CancellationToken cancellationToken)
        {
            var phase = new GtfsImportPhase
            {
                GtfsImportRunId = runId,
                PhaseName = phaseName,
                StartedAt = DateTime.UtcNow,
                ProgressPercentage = 0,
                ProcessedRecordCount = 0
            };
            context.GtfsImportPhases.Add(phase);
            await context.SaveChangesAsync(cancellationToken);
            return phase;
        }

        private async Task UpdatePhaseAsync(AppDbContext context, GtfsImportPhase phase, int percentage, int recordCount, CancellationToken cancellationToken)
        {
            phase.ProgressPercentage = percentage;
            phase.ProcessedRecordCount = recordCount;
            context.GtfsImportPhases.Update(phase);
            await context.SaveChangesAsync(cancellationToken);
        }

        private async Task CompletePhaseAsync(AppDbContext context, GtfsImportPhase phase, CancellationToken cancellationToken)
        {
            phase.ProgressPercentage = 100;
            phase.FinishedAt = DateTime.UtcNow;
            context.GtfsImportPhases.Update(phase);
            await context.SaveChangesAsync(cancellationToken);
        }

    }
}

