# Test Raporu (Test Coverage)

Bu doküman projenin test kapsamını (coverage) ve durumunu özetlemektedir.

## Genel Durum
Projedeki Unit ve Integration testleri `dotnet test` komutuyla düzenli olarak koşturulmakta ve CI pipeline üzerinden takip edilmektedir.

- **Toplam Test Sayısı:** 105
- **Başarılı (Passed):** 105
- **Başarısız (Failed):** 0
- **Atlanan (Skipped):** 0

## Integration Testleri ve Testcontainers Uyarısı
Tüm entegrasyon testleri `Testcontainers.PostgreSql` kütüphanesi kullanarak gerçek bir PostgreSQL veritabanı üzerinde koşmaktadır.
> [!WARNING]
> Entegrasyon testlerinin yerel ortamda (local) çalışabilmesi için bilgisayarınızda **Docker Desktop** veya Docker Engine'in çalışır durumda olması ZORUNLUDUR. Eğer Docker yüklü değilse, `DotNet.Testcontainers.Builders.DockerUnavailableException` hatası alırsınız. Bu testler GitHub Actions CI ortamında sorunsuz çalışmaktadır.

## Test Edilen Kritik Senaryolar
- **Eşzamanlılık (Concurrency):** Aynı anda yapılan iki Import işleminin `ConcurrentImportException` (409 Conflict) vererek engellendiği test edilmiştir.
- **Güvenli Hata Yönetimi (Security):** `ExceptionMiddleware` test edilmiş ve `ProblemDetails` dışarıya StackTrace veya SQL bağlantı hatası sızdırmadığı doğrulanmıştır.
- **Rollback ve Hata İzolasyonu:** GTFS dosyası bozuk olduğunda (Invalid CSV) hedeflenen ana tabloların Rollback edildiği, ancak hatanın kendisinin ayrı bir DbContext Scope üzerinden "Failed" olarak persist edildiği doğrulanmıştır.
- **Dış Servis Hataları (External Service):** ESHOT API'si timeout olduğunda 503 Service Unavailable, bozuk json döndüğünde 502 Bad Gateway verildiği doğrulanmıştır.
