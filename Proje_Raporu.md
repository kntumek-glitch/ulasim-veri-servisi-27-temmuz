# Ulaşım Veri Servisi (Transportation Data Service) - Proje Raporu
 

## 1. Projenin Amacı ve Kapsamı
Bu proje, İzmir ili (ESHOT vb.) ulaşım verilerini GTFS (General Transit Feed Specification) formatında alarak, istemcilere (Mobil/Web uygulamalar) dinamik, tutarlı ve yüksek performanslı şekilde sunan bir backend servisinin (API) geliştirilmesini amaçlamaktadır. 

Mevcut ulaşım verilerinin yönetilmesindeki zorluklar, veri büyüklüğü ve senkronizasyon problemleri göz önünde bulundurularak "Clean Architecture" prensiplerine uygun, ölçeklenebilir ve güvenli bir sistem tasarlanmıştır.

## 2. Geliştirilen Mimari ve Teknik Özellikler

### 2.1. Dinamik GTFS Veri Yönetimi
- **Tarih Bazlı Sefer Hesaplaması:** İstemcilerin belirli bir tarihte hangi otobüslerin hareket edeceğini görebilmeleri için `calendar.txt` ve `calendar_dates.txt` (Exception günleri) dosyaları kullanılarak özel bir algoritma geliştirilmiştir. Tatil günleri, iptal edilen seferler veya özel eklenen seferler otomatik algılanmaktadır. 24:00:00'ı aşan gece seferleri (Örn: 25:30:00) standart saat dilimlerine parse edilerek veri bütünlüğü sağlanmıştır.
- **GeoJSON Desteği:** Harita entegrasyonlarını kolaylaştırmak adına güzergah koordinatları (Shapes), `tripId` veya `patternId` filtreleri kullanılarak endüstri standardı olan GeoJSON (LineString) formatında istemciye sunulmaktadır.

### 2.2. Performans ve Ölçeklenebilirlik Altyapısı
- **ETag ve Dinamik Cache Yönetimi:** Sistem, gereksiz veri transferini ve ağ trafiğini (bandwidth) önlemek için ETag mimarisi ile donatılmıştır. İstemciler aynı veriyi tekrar istediklerinde HTTP 304 (Not Modified) yanıtı alırlar. Hash bazlı Cache invalidation mekanizması ile sunucu belleği (RAM) optimize edilmiştir.
- **Asenkron Veri İşleme (Batch Processing):** Yaklaşık 1 milyona varan durak-zaman (stop_times) verileri Entity Framework Core kullanılarak 500'lük chunk'lar (batch) halinde belleği taşırmadan veritabanına aktarılmaktadır.

### 2.3. Sistem Güvenliği ve Altyapı Denetimi (DevOps)
- **Admin Key Yetkilendirmesi:** Veritabanını mutasyona uğratan Import ve Mutabakat (Reconciliation) işlemleri, basit ama etkili bir `X-Admin-Key` mimarisi ile koruma altına alınmıştır. Anahtarlar statik koda gömülmemiş, "Environment Variables" (Çevre değişkenleri) üzerinden okunacak şekilde izole edilmiştir.
- **Konteyner ve Monitoring Uyumlu Health-Checks:** Uygulama sağlık denetimleri mikroservis standartlarında Liveness (`/health/live`), Readiness (`/health/ready`) ve Dependencies (`/health/dependencies`) olarak 3 ayrı uç noktaya bölünmüştür. Dış API'ler çöktüğünde sistem tamamen kapanmak yerine, "Degraded" duruma geçerek ana akışını sürdürmektedir.

## 3. Otomatik Test Süreçleri ve Kalite Güvencesi (QA)
Projenin hata toleransını ölçmek ve CI/CD (Sürekli Entegrasyon) süreçlerinde güvenle çalışabilmesini sağlamak adına **xUnit** tabanlı Entegrasyon testleri yazılmıştır:
1. **Import Lifecycle Testleri:** Veri yükleme işleminin başarılı (Completed) veya hatalı (Failed) olma durumları simüle edilmiş, eşzamanlı isteklerde (Race Condition) çakışmaları önleyen mimari test edilmiştir.
2. **Fallback (Geri Dönüş) Mekanizması:** Yeni veri import edilirken bir hata oluşursa, sistemin çökmediği ve eski stabil veri üzerinden (Completed status) istemcilere yanıt vermeye devam ettiği doğrulanmıştır.
3. **Güvenli Hata Yönetimi (RFC 7807):** Sistem içi 500 (Internal Server Error) veya 404 (Not Found) hatalarında, dışarıya güvenlik açığı oluşturabilecek (Stack Trace) bilgilerin sızmadığı ve standart `ProblemDetails` JSON objesinin döndüğü test edilmiştir.

## 4. Sonuç ve Gelecek Geliştirmeler
Sistem; performans, güvenlik ve kod mimarisi (Clean Architecture) açısından belirlenen kabul kriterlerini başarıyla karşılamıştır. 
Projenin kaynak kodları, çalışır durumdaki Docker yapılandırmaları ve Swagger (OpenAPI) dokümantasyonu entegre bir biçimde repoda sunulmaktadır. Gelecek fazlarda gerçek zamanlı otobüs konumları için WebSocket tabanlı bir altyapının entegre edilmesi hedeflenmektedir.
