# Ulaşım Veri Servisi (Transportation Data Service)

Bu proje, İzmir (ESHOT vb.) ulaşım verilerini, GTFS formatlı veritabanı senkronizasyonunu ve canlı veri akışını yöneten merkezi backend servisidir.

## 🚀 Son Eklenen Özellikler (Faz 2 Teslimatları)

1. **GTFS Departures (Tarih Bazlı Seferler):** `calendar.txt` ve `calendar_dates.txt` üzerinden geçerlilik kuralları (exception 1 ve 2) işletilerek tam doğru sefer kalkış saatleri API'ye bağlandı. 24 saati geçen sefer saatleri de (Örn: 25:30:00) doğru şekilde parse edilerek entegre edildi.
2. **GeoJSON & Pattern Shape İyileştirmeleri:** `/api/v1/gtfs/shapes` üzerinden `tripId` veya `patternId` filtreleri ile harita koordinatları GeoJSON (LineString) desteği ile alınabilir hale getirildi. 
3. **ETag, Cache ve Metadata Altyapısı:** `X-Admin-Key` güvenlik katmanıyla korunan Import işlemleri ile beslenen veritabanı, `/api/v1/gtfs/metadata` ile sürümlendirildi. Dinamik Cache Keys ve HTTP 304 Not Modified (ETag) özellikleri kullanılarak ciddi bant genişliği tasarrufu sağlandı.
4. **DevOps & Kubernetes Health-Checks:** Uygulama sağlık denetimleri `/health/live`, `/health/ready`, `/health/dependencies` olarak 3 ayrı mikroservis standardına bölündü.
5. **Genişletilmiş QA & xUnit Testleri:** Lifecycle (Completed/Failed state), Concurrency, Fallback ve Exception (ProblemDetails sızıntısı olmayan hata yönetimi) testleri tam otomasyona bağlandı.

## ⚙️ Güvenlik ve Kurulum Kuralları
- **Admin API Key:** Veritabanını mutasyona uğratan Import ve Reconcile endpoint'leri `X-Admin-Key` gerektirir. Bu anahtarı hiçbir zaman koda yazmayınız, Docker `.env` dosyası üzerinden `AdminSettings__ApiKey` env variable ile sunucuya veriniz.

## 🔗 Endpoint Listesi ve İletişim
Servisin tam Swagger dokümantasyonu geliştirme (Development) ortamında `/swagger` yolu üzerinden otomatik ayağa kalkar. Örnek kullanım ve payload boyut testleri için test/integration dizinine bakabilirsiniz.
