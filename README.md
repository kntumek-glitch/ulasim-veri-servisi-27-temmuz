# Ulaşım Veri Servisi (Transportation Data Service)

Bu proje, İzmir (ESHOT vb.) ulaşım verilerini, GTFS formatlı veritabanı senkronizasyonunu ve canlı veri akışını yöneten merkezi backend servisidir.

## 🚀 Son Eklenen Özellikler (Faz 4 Teslimatları)

1. **Zaman ve Aktarma Duyarlı Yolculuk Planlama (Journey Planning):** `/api/v1/journey-plans/search` endpoint'i üzerinden A noktasından B noktasına gitmek için statik GTFS verisi kullanılarak rotalar oluşturulmaktadır. 
2. **Yürüyüş ve Aktarma Optimizasyonları:** Kullanıcı başlangıç noktasından ilk durağa yürüme hızı (`1.4 m/s`) ile hesaplanır. Spatial Grid algoritmasıyla sadece yürüme mesafesindeki (`MaxTransferWalkMeters`) duraklara aktarma desteklenir (1-Aktarma).
3. **Gün Aşan (Cross-day) Senaryolar ve 24 Saat Üzeri Saatler:** 25:30:00 (01:30) gibi gece yarısını geçen seferler, `ServiceDate` konsepti ile birlikte doğru takvim günü baz alınarak çözümlenir ve aktarmalar kusursuz işletilir.
4. **Performans (Grid Spatial Caching):** Yakın durak aramaları `O(N)` yerine önceden indekslenmiş koordinat tabanlı grid'ler kullanılarak `O(1)` karmaşıklığında 10ms altında çözümlenmektedir.
5. **Kapsamlı Integration Testleri:** Tüm modül (138 test), gerçek bir PostgreSQL `Testcontainers` kullanılarak veri bütünlüğü ve rollback senaryoları çerçevesinde baştan uca test edilmektedir.

## ⚙️ Güvenlik ve Kurulum Kuralları
- **Admin API Key:** Veritabanını mutasyona uğratan Import ve Reconcile endpoint'leri `X-Admin-Key` gerektirir. Bu anahtarı hiçbir zaman koda yazmayınız, Docker `.env` dosyası üzerinden `AdminSettings__ApiKey` env variable ile sunucuya veriniz.

## 🔗 Endpoint Listesi ve İletişim
Servisin tam Swagger dokümantasyonu geliştirme (Development) ortamında `/swagger` yolu üzerinden otomatik ayağa kalkar. Örnek kullanım ve payload boyut testleri için test/integration dizinine bakabilirsiniz.
