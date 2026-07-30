# Journey Planning Performans ve Optimizasyon (Faz 4)

Yolculuk planlama API'si (`/api/v1/journey-plans/search`), milyonlarca `GtfsStopTime` satırı üzerinde anlık hesaplama yaptığı için performans ve bellek kısıtlamalarına karşı özel bir dizayna ihtiyaç duymuştur. Aşağıda projeye entegre edilen mimari stratejiler yer almaktadır.

## 1. Veritabanı ve İndeksleme (Database & Indexing)
Yüksek trafikli aramalarda "Full Table Scan (Sıralı Tarama)" engellemek amacıyla `AppDbContext.cs` üzerinde aşağıdaki kompozit (Composite) indeksler kullanılmıştır:

- **`[GtfsStopTime_GtfsTripId_StopSequence]` İndeksi:** Bir biniş durağından sonra aracın hangi duraklara gideceğini hızla bulmak için (Yön kontrolü).
- **`[GtfsStopTime_StopId_DepartureSeconds]` İndeksi:** Kullanıcının kalkış durağından belirli bir saatten sonra geçen araçları (Leg1) anında getirmek için. Bu indeks, sorgu süresini `~400ms`'den `<50ms`'ye düşürmüştür.

Bunun yanı sıra `Tracking` maliyetini önlemek için tüm Entity Framework okuma sorgularında `.AsNoTracking()` metodu varsayılan olarak uygulanmıştır.

## 2. Aktif Feed Kilitlemesi (Active Feed Locking)
Sistemde birden fazla (eski) GTFS ithalatı (ImportRun) bulunabilir. `IsActive = true` olan tek bir import'un ID'si (ActiveImportId) üzerinden sorgulama yapılarak pasif durakların/seferlerin gereksiz yere belleğe veya `JOIN` maliyetlerine dahil olması PostgreSQL düzeyinde kısıtlanmıştır.

## 3. Önbellekleme (IMemoryCache) Stratejisi
Yolculuk planlama sonuçları her zaman eşsiz koordinatlara (Origin/Dest) sahip olduğu için, önbellekleme yapılırken çok boyutlu ve esnek bir Anahtar (Cache Key) algoritması geliştirilmiştir:

```
Key Formatı: JourneyPlan_{OriginLat}_{OriginLon}_{DestLat}_{DestLon}_{TimeBucket}_{ActiveFeedId}
```

**Optimizasyonlar:**
1. **Zaman Sepeti (Time Bucket):** Kullanıcının 08:00 ile 08:04 arasında yapacağı aramalar aynı "5 dakikalık sepete" (TimeBucket) dahil edilerek yüksek oranda Cache Hit yakalanması hedeflenir.
2. **Aktif Feed Değişimi:** GTFS verisi güncellendiğinde (`ActiveFeedId` değiştiğinde), cache anahtarı değişeceği için eski veri (stale cache) dönmesi imkansızlaştırılmıştır. Cache temizleme mekanizmasına (Cache Invalidation) gerek kalmamıştır.
3. **Cache Memory Limit (SizeLimit):** Servisin `OOM (Out Of Memory)` hatası vermesini engellemek için, `IMemoryCache` konfigürasyonuna Global `SizeLimit` atanmış ve her cache kaydına `Size = 1` değeri verilerek toplam kapasite kontrol altına alınmıştır.

## 4. İstemci İptalleri ve Kaynak Tasarrufu (Cancellation Token)
Kullanıcılar genellikle arama sonuçlarını beklemeden sekmeyi veya uygulamayı kapatabilirler (Özellikle mobil).
Backend, tüm sorgularına `CancellationToken` (İstemci Kapatma Sinyali) entegre etmiştir. İstemci HTTP isteğini kopardığında (HTTP 499), veritabanındaki uzun süren aktarma (Transfer) aramaları anında abort edilerek (İptal edilerek) CPU ve RAM kaynaklarının israf edilmesi önlenir.
(Bkz: `E3_LongRunningQuery_ShouldBe_Cancelled` uçtan uca testi).
