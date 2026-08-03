# Ulaşım Veri Servisi (Transportation Data Service)

Bu proje, İzmir (ESHOT vb.) ulaşım verilerini, GTFS formatlı veritabanı senkronizasyonunu ve canlı veri akışını yöneten merkezi backend servisidir.

## 🚀 Son Eklenen Özellikler (Faz 5 Teslimatları)

1. **Zaman ve Aktarma Duyarlı Yolculuk Planlama (Journey Planning):** `/api/v1/journey-plans/search` endpoint'i üzerinden A noktasından B noktasına gitmek için statik GTFS verisi kullanılarak rotalar oluşturulmaktadır. Artık **maksimum 2 aktarmalı** seyahatler tam desteklenmektedir.
2. **Kalıcı Transfer Ağı (Persistent Transfer Network):** Aktarma yapılabilecek duraklar, O(N^2) hesaplama yerine önceden (background task olarak) veritabanına indekslenir. Spatial Grid (Haversine) algoritmasıyla sadece yürüme mesafesindeki duraklara aktarma imkanı tanınır.
3. **Ara Duraklar ve Güzergah Detayları:** Arama sonuçlarında, binilen ve inilen duraklar arasındaki tüm ara duraklar (`includeIntermediateStops` parametresi ile) ve güzergah çizimi için `shapeId` bilgisi döndürülür.
4. **Gün Aşan (Cross-day) Senaryolar ve Gece Seferleri:** 25:30:00 (01:30) gibi gece yarısını geçen seferler, `ServiceDate` konsepti ile birlikte doğru takvim günü baz alınarak çözümlenir ve aktarmalar (1 ve 2 aktarmalı senaryolar dahil) kusursuz işletilir.
5. **Kapsamlı Integration Testleri:** Tüm modül (163 test), gerçek bir PostgreSQL `Testcontainers` kullanılarak veri bütünlüğü ve rollback senaryoları çerçevesinde baştan uca test edilmektedir. Tüm testler birbirini ezmeden izolasyonlu çalışır.
6. **Performans ve Hata Yönetimi:** API seviyesinden veritabanına kadar tam `CancellationToken` entegrasyonu vardır ve hatalar endüstri standardı RFC 7807 (`ProblemDetails`) ile sunulmaktadır.

## 🛡️ Güvenlik ve Kurulum Kuralları
- **Admin API Key:** Veritabanını mutasyona uğratan Import, Reconcile ve Rebuild Transfers endpoint'leri `X-Admin-Key` gerektirir. Bu anahtarı hiçbir zaman koda yazmayınız, Docker `.env` dosyası üzerinden `AdminSettings__ApiKey` env variable ile sunucuya veriniz.

## 📡 Endpoint Listesi ve İletişim
Servisin tam Swagger dokümantasyonu geliştirme (Development) ortamında `/swagger` yolu üzerinden otomatik ayağa kalkar. Örnek kullanım ve testler için test/integration dizinine bakabilir, detaylı algoritma açıklamaları için `docs/` klasöründeki belgelere göz atabilirsiniz.
