# Journey Planning Performans ve Optimizasyon (Faz 5)

Yolculuk planlama API'si (`/api/v1/journey-plans/search`), milyonlarca `GtfsStopTime` satırı üzerinde (özellikle 2 aktarmalı senaryolarda) karmaşık hesaplama yaptığı için performans ve bellek (RAM) kısıtlamalarına karşı özel bir mimariyle dizayn edilmiştir.

## 1. Veritabanı ve İndeksleme (Database & Indexing)
Yüksek trafikli aramalarda "Full Table Scan (Sıralı Tarama)" engellemek amacıyla `AppDbContext.cs` üzerinde aşağıdaki kompozit (Composite) indeksler kullanılmıştır:

- **`[GtfsStopTime_GtfsTripId_StopSequence]` İndeksi:** Bir biniş durağından sonra aracın hangi duraklara gideceğini hızla bulmak için (Yön kontrolü).
- **`[GtfsStopTime_StopId_DepartureSeconds]` İndeksi:** Kullanıcının kalkış durağından belirli bir saatten sonra geçen araçları (Leg1) anında getirmek için. 
- **`[GtfsTransfers_RunId_FromStop_ToStop]` (Yeni Faz 5):** Aktarma bağlantılarını O(1) sürede getirmek için kalıcı transfer ağına uygulanan Primary Key indeksidir.

Tüm Entity Framework okuma sorgularında `Tracking` maliyetini önlemek için `.AsNoTracking()` metodu varsayılan olarak uygulanmıştır.

## 2. Kalıcı Transfer Ağı ile Pre-calculation (O(N²) Önleme)
1 aktarmalı ve 2 aktarmalı rotalardaki en büyük darboğaz, potansiyel aktarma duraklarının birbirleriyle (N x N x N) karşılaştırılmasıdır (Brute-force Haversine mesafe hesaplaması).
Bu problemi çözmek ve zaman karmaşıklığını API isteği anında lineer $O(N)$ seviyesine çekmek için Faz 5 ile birlikte **Kalıcı Transfer Ağı (Persistent Transfer Network)** entegre edilmiştir.
- **Background İşlemi:** Durakların birbirine olan mesafesi (Haversine & Spatial Grid) Import veya Rebuild işlemi sırasında asenkron olarak hesaplanır ve `GtfsTransfers` tablosuna yazılır.
- **Sorgu Anı:** İstek geldiğinde, mesafe formülü hesaplanmaz; sadece indekslenmiş tablodan Join/Include ile ilişkili duraklar çekilir.

## 3. İstemci İptalleri ve Kaynak Tasarrufu (CancellationToken)
Kullanıcılar (özellikle mobil istemcilerde) rota sonuçlarını beklemeden sekmeyi veya sayfayı kapatabilirler.
- API'deki tüm uç noktalardan başlayıp, Service ve Entity Framework (Veritabanı/HttpClient) katmanının en derinine kadar `CancellationToken` (İstemci Kapatma Sinyali) iletilmektedir.
- İstemci HTTP isteğini kopardığında (Örn: HTTP 499), veritabanındaki ağır `ToListAsync`, `CountAsync`, `FirstOrDefaultAsync` çağrıları anında abort edilerek (iptal edilerek) sunucu CPU ve RAM kaynaklarının israf edilmesi önlenir.

## 4. Aktif Feed Kilitlemesi (Active Feed Locking)
Sistemde birden fazla (eski) GTFS ithalatı (ImportRun) bulunabilir. `IsActive = true` olan tek bir import'un ID'si (ActiveImportId) üzerinden sorgulama yapılarak pasif durakların/seferlerin gereksiz yere belleğe veya `JOIN` maliyetlerine dahil olması PostgreSQL düzeyinde kısıtlanmıştır.

## 5. Önbellekleme (IMemoryCache) Stratejisi
Yolculuk planlama sonuçları, esnek bir Anahtar (Cache Key) algoritması ile önbelleklenir:

```
Key Formatı: JourneyPlan_{OriginLat}_{OriginLon}_{DestLat}_{DestLon}_{TimeBucket}_{ActiveFeedId}
```

**Optimizasyonlar:**
1. **Zaman Sepeti (Time Bucket):** Kullanıcının 08:00 ile 08:04 arasında yapacağı aramalar aynı "5 dakikalık sepete" (TimeBucket) dahil edilerek yüksek oranda Cache Hit yakalanması hedeflenir.
2. **Aktif Feed Değişimi:** GTFS verisi güncellendiğinde (`ActiveFeedId` değiştiğinde), cache anahtarı değişeceği için eski veri (stale cache) dönmesi imkansızlaştırılmıştır. 
3. **Cache Memory Limit (SizeLimit):** Global `SizeLimit` atanmış ve her cache kaydına `Size = 1` değeri verilerek toplam RAM kapasitesi kontrol altına alınmıştır.

## 6. Ara Durak (Intermediate Stops) Bandwidth Optimizasyonu
API yanıtlarında ara durakların ve Shape noktalarının (Harita Çizgileri) döndürülmesi ağ ve veri işleme (bandwidth/JSON serialization) yükünü artırır. 
Geliştirilen Opt-in yapı sayesinde, varsayılan yanıtta (`IncludeIntermediateStops=false`) hiçbir ara durak hesabı/sorgusu yapılmaz.
Aynı parametrelerle yapılan payload (JSON Boyutu) testlerinde:
- **`IncludeIntermediateStops = false`:** ~1.34 KB
- **`IncludeIntermediateStops = true`:** ~2.50 KB (Ara duraklarla 2 kat, ShapePoint'lerle çok daha büyük olabilir)
Bu nedenle "ara duraklı" veri sadece UI'da rota detayına girildiğinde talep edilmelidir.

## 7. Örnek Benchmark Sonuçları
Büyük bir GTFS veri setinde (Örn: İzmir ESHOT, ~3.5 Milyon StopTime satırı) Kalıcı Transfer ağı ve İndeksleme sonrası gerçek EXPLAIN ANALYZE sonuçları ve Execution süreleri:

| Senaryo (Local PostgreSQL)  | Ortalama Süre (Warm Cache Hariç) | Veritabanı Tarama Yöntemi |
|-----------------------------|----------------------------------|---------------------------|
| Doğrudan (0-Transfer)       | ~25 - 50 ms                      | Index Scan                |
| Aktarmalı (1-Transfer)      | ~60 - 100 ms                     | Nested Loop w/ Index      |
| Aktarmalı (2-Transfer)      | ~150 - 300 ms                    | Hash Join & Index Scan    |
| Bulunamayan (Not Found)     | ~10 ms (Erken Ret)               | Index Scan (0 Hits)       |

2-Aktarmalı rotaların 300ms altında dönebilmesi, tamamen `GtfsTransfers` tablosunun önceden hesaplanmış (Pre-calculated) olmasına dayanmaktadır.
