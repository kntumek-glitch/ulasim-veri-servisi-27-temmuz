using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Linq.Expressions;
using TransportDataService;
using TransportDataService.Domain;

namespace ulasim_veri_servisi.Services
{
    public class CsvImportService
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;

        public CsvImportService(
            AppDbContext context,
            HttpClient httpClient)
        {
            _context = context;
            _httpClient = httpClient;
        }
        public async Task<ImportResult> ImportAsync(CancellationToken cancellationToken)
        {
            int importedCount = 0;
            int updatedCount = 0;
            int failedCount = 0;
            const int batchSize = 100;
            int processedCount = 0; 
            var startedAt = DateTime.UtcNow;
            var errorDetails = new List<string>();

            try
            {
                var url = "https://openfiles.izmir.bel.tr/211488/docs/eshot-otobus-duraklari.csv";
                var csvText = await _httpClient.GetStringAsync(url, cancellationToken);

                using var reader = new StringReader(csvText);
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = ",",
                    MissingFieldFound = null,
                    HeaderValidated = null
                };

                using var csv = new CsvReader(reader, config);
                
                await csv.ReadAsync();
                csv.ReadHeader();
                var headers = csv.HeaderRecord;
                
                if (headers == null)
                    throw new Exception("CSV header okunamadı.");

                string? idColumn = headers.Contains("DURAK_ID") ? "DURAK_ID" : (headers.Contains("sDURAK_ID") ? "sDURAK_ID" : null);
                if (idColumn == null)
                    throw new Exception("Geçersiz CSV header yapısı: Durak ID kolonu bulunamadı.");

                // Pre-fetch all stops for lookup
                var allStops = _context.Stops.ToList();
                var allRoutes = _context.StopRoutes.ToList();

                var stopsDict = allStops.ToDictionary(s => s.ExternalStopId);
                var routesDict = allRoutes
                    .GroupBy(r => r.StopId)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.RouteNumber).ToHashSet());

                int rowNumber = 1; // Header is 1
                
                while (await csv.ReadAsync())
                {
                    rowNumber++;
                    try
                    {
                        string durakId = csv.GetField<string>(idColumn) ?? string.Empty;
                        string durakAdi = csv.GetField<string>("DURAK_ADI") ?? string.Empty;
                        string enlem = csv.GetField<string>("ENLEM") ?? string.Empty;
                        string boylam = csv.GetField<string>("BOYLAM") ?? string.Empty;
                        string hatlar = csv.GetField<string>("DURAKTAN_GECEN_HATLAR") ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(durakId))
                        {
                            throw new Exception("Durak ID boş olamaz.");
                        }

                        if (!stopsDict.TryGetValue(durakId, out var stop))
                        {
                            stop = new Stop
                            {
                                ExternalStopId = durakId,
                                CreatedAt = DateTime.UtcNow
                            };
                            _context.Stops.Add(stop);
                            stopsDict[durakId] = stop;
                            importedCount++;
                        }
                        else
                        {
                            updatedCount++;
                        }

                        stop.Name = durakAdi;

                        if (double.TryParse(enlem.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var latitude)
                            && latitude >= -90 && latitude <= 90)
                        {
                            stop.Latitude = latitude;
                        }
                        else
                        {
                            stop.Latitude = null;
                        }

                        if (double.TryParse(boylam.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var longitude)
                            && longitude >= -180 && longitude <= 180)
                        {
                            stop.Longitude = longitude;
                        }
                        else
                        {
                            stop.Longitude = null;
                        }

                        stop.UpdatedAt = DateTime.UtcNow;

                        var routeList = string.IsNullOrWhiteSpace(hatlar) 
                            ? Array.Empty<string>() 
                            : hatlar.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                    .Distinct()
                                    .ToArray();

                        // Only add route if not in lookup, but wait - stop might not have an ID yet if it's new
                        // So we track new routes in the context directly
                        var existingRoutes = stop.Id > 0 && routesDict.TryGetValue(stop.Id, out var rSet) 
                            ? rSet 
                            : new HashSet<string>();

                        foreach (var route in routeList)
                        {
                            if (!existingRoutes.Contains(route))
                            {
                                // We check _context.ChangeTracker or local newly added ones
                                // By using stop.StopRoutes navigation property, EF handles it
                                if (!stop.StopRoutes.Any(x => x.RouteNumber == route))
                                {
                                    stop.StopRoutes.Add(new StopRoute
                                    {
                                        Stop = stop,
                                        RouteNumber = route,
                                        CreatedAt = DateTime.UtcNow
                                    });
                                }
                            }
                        }

                        processedCount++;
                        if (processedCount % batchSize == 0)
                        {
                            await _context.SaveChangesAsync(cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        errorDetails.Add($"Satır {rowNumber}: {ex.Message}");
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);

                string finalStatus = failedCount > 0 ? "CompletedWithErrors" : "Completed";
                string? finalErrorMessage = errorDetails.Any() ? string.Join(" | ", errorDetails.Take(10)) + (errorDetails.Count > 10 ? "..." : "") : null;

                var importRun = new ImportRun
                {
                    SourceName = "ESHOT Otobüs Durakları CSV",
                    StartedAt = startedAt,
                    FinishedAt = DateTime.UtcNow,
                    ImportedRecordCount = importedCount,
                    UpdatedRecordCount = updatedCount,
                    FailedRecordCount = failedCount,
                    Status = finalStatus,
                    ErrorMessage = finalErrorMessage
                };

                _context.ImportRuns.Add(importRun);
                await _context.SaveChangesAsync(cancellationToken);

                return new ImportResult
                {
                    SourceName = "ESHOT Otobüs Durakları CSV",
                    ImportedRecordCount = importedCount,
                    UpdatedRecordCount = updatedCount,
                    FailedRecordCount = failedCount,
                    Status = finalStatus,
                    StartedAt = startedAt,
                    FinishedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                var importRun = new ImportRun
                {
                    SourceName = "ESHOT Otobüs Durakları CSV",
                    StartedAt = startedAt,
                    FinishedAt = DateTime.UtcNow,
                    ImportedRecordCount = importedCount,
                    UpdatedRecordCount = updatedCount,
                    FailedRecordCount = failedCount,
                    Status = "Failed",
                    ErrorMessage = ex.Message
                };

                _context.ImportRuns.Add(importRun);
                await _context.SaveChangesAsync(cancellationToken);

                throw;
            }
        }
    }
}

