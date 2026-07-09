using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using TransportDataService;
using TransportDataService.Domain;

namespace ulasım_veri_servisi.Services
{
    public class CsvImportService
    {
        private readonly AppDbContext _context;

        public CsvImportService(AppDbContext context)
        {
            _context = context;
        }
        public ImportResult Import()
        {
            var startedAt = DateTime.UtcNow;

            int importedCount = 0;
            int updatedCount = 0;
            int failedCount = 0;
            var url = "https://openfiles.izmir.bel.tr/211488/docs/eshot-otobus-duraklari.csv";

            using var client = new HttpClient();

            var csvText = client.GetStringAsync(url).Result;

            using var reader = new StringReader(csvText);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ";"
            };

            using var csv = new CsvReader(reader, config);

            var records = csv.GetRecords<dynamic>().ToList();


            foreach (var record in records)
            {
                Console.WriteLine(record);
                string durakId = record.DURAK_ID.ToString();
                string durakAdi = record.DURAK_ADI.ToString();
                string enlem = record.ENLEM.ToString();
                string boylam = record.BOYLAM.ToString();
                string hatlar = record.DURAKTAN_GECEN_HATLAR.ToString();

                // Şimdilik burada duracağız.

                var stop = _context.Stops.FirstOrDefault(x => x.ExternalStopId == durakId);

                if (stop == null)
                {
                    stop = new Stop
                    {
                        ExternalStopId = durakId,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Stops.Add(stop);

                    // Önce Stop'u veritabanına kaydet ki Id oluşsun
                    _context.SaveChanges();
                    importedCount++;
                }
                else
                {
                    updatedCount++;
                }

                stop.Name = record.DURAK_ADI;
                stop.Latitude = double.Parse(record.ENLEM);
                stop.Longitude = double.Parse(record.BOYLAM);
                stop.UpdatedAt = DateTime.UtcNow;
                
                var routeList = hatlar.Split(',');

                foreach (var route in routeList)
                {
                    var routeNumber = route.Trim();

                    if (!_context.StopRoutes.Any(x =>
                        x.StopId == stop.Id &&
                        x.RouteNumber == routeNumber))
                    {
                        _context.StopRoutes.Add(new StopRoute
                        {
                            StopId = stop.Id,
                            RouteNumber = routeNumber,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

            }
            _context.SaveChanges();
            var importRun = new ImportRun
            {
                SourceName = "ESHOT Otobüs Durakları CSV",
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow,
                ImportedRecordCount = importedCount,
                UpdatedRecordCount = updatedCount,
                FailedRecordCount = failedCount,
                Status = "Completed"
            };

            _context.ImportRuns.Add(importRun);
            _context.SaveChanges();
            return new ImportResult
            {
                SourceName = "ESHOT Otobüs Durakları CSV",
                ImportedRecordCount = importedCount,
                UpdatedRecordCount = updatedCount,
                FailedRecordCount = failedCount,
                Status = "Completed",
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow
            };
        }
    }
}
