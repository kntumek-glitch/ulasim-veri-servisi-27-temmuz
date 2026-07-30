# Journey Planning API Contract

Bu doküman, Ulaşım Veri Servisi (Faz 4) kapsamında geliştirilen `/api/v1/journey-plans/search` endpoint'inin İstek (Request) ve Yanıt (Response) yapısını açıklar.

## 1. İstek (Request) Yapısı

API, kullanıcıdan rotanın başlangıç/bitiş koordinatlarını, ayrılış saatini ve arama yapılandırmalarını JSON formatında alır.

### HTTP Endpoint
`POST /api/v1/journey-plans/search`

### JSON Request
```json
{
  "origin": {
    "lat": 38.4,
    "lon": 27.1
  },
  "destination": {
    "lat": 38.41,
    "lon": 27.11
  },
  "departureDateTime": "2024-01-01T08:00:00+03:00",
  "maxWalkingMeters": 1500,
  "maxResults": 10
}
```

#### Parametreler
- `origin` ve `destination`: Başlangıç ve varış noktalarının enlem ve boylam değerleri. (Zorunlu)
- `departureDateTime`: İstenen başlangıç tarihi ve saati. `DateTimeOffset` (ISO 8601) formatında olmalıdır. (Zorunlu)
- `maxWalkingMeters`: Kullanıcının katlanabileceği maksimum yürüme mesafesi (metre). Opsiyoneldir, default: 1500.
- `maxResults`: Döndürülecek maksimum rota (itinerary) sayısı. Opsiyoneldir, default: 10.

## 2. Yanıt (Response) Yapısı

API, sonuç olarak metadata (veri sağlayıcı kaynağı bilgileri) ve rota (Itinerary) alternatiflerini içeren detaylı bir JSON döner.

### Başarılı Yanıt Örneği (200 OK)
```json
{
  "metadata": {
    "activeImportId": 999,
    "feedHash": "8A2B...90C",
    "startDate": "2024-01-01",
    "endDate": "2024-12-31",
    "isStale": false,
    "timezone": "Europe/Istanbul",
    "dataSourceWarning": "Sonuçlar statik (planlı) tarife verisine dayanmaktadır, canlı araç konumu/trafiği içermez."
  },
  "reasonCode": "SUCCESS",
  "itineraries": [
    {
      "planId": "72b522c6-f6b9-42d4-bd29-c6b727a8a3fb",
      "departureTime": "2024-01-01T08:00:00+03:00",
      "arrivalTime": "2024-01-01T08:38:00+03:00",
      "totalDurationMinutes": 38,
      "transfers": 0,
      "totalWalkingMeters": 700,
      "totalWalkingMinutes": 8,
      "serviceDate": "2024-01-01",
      "totalTransitStops": 5,
      "legs": [
        {
          "mode": "WALK",
          "fromStopId": "ORIGIN",
          "fromStopName": "Mevcut Konum",
          "toStopId": "S1",
          "toStopName": "Origin",
          "distanceMeters": 350,
          "durationMinutes": 4
        },
        {
          "mode": "TRANSIT",
          "routeId": "R1",
          "routeShortName": "100",
          "tripId": "T1",
          "directionId": 0,
          "headsign": "Dest",
          "fromStopId": "S1",
          "fromStopName": "Origin",
          "fromStopSequence": 1,
          "toStopId": "S3",
          "toStopName": "Dest",
          "toStopSequence": 6,
          "rawGtfsDepartureTime": "08:04:00",
          "departureTime": "2024-01-01T08:04:00+03:00",
          "rawGtfsArrivalTime": "08:34:00",
          "arrivalTime": "2024-01-01T08:34:00+03:00",
          "intermediateStopCount": 4,
          "distanceMeters": 0,
          "durationMinutes": 30,
          "stopCount": 5
        },
        {
          "mode": "WALK",
          "fromStopId": "S3",
          "fromStopName": "Dest",
          "toStopId": "DEST",
          "toStopName": "Varış Noktası",
          "distanceMeters": 350,
          "durationMinutes": 4
        }
      ]
    }
  ]
}
```

#### Parametreler
- `Metadata`: GTFS veri seti hakkında geçerlilik, tarih ve versiyon bilgileri sunar.
- `ReasonCode`: İşlem sonucunu belirtir. Eğer hiçbir rota bulunamazsa (Deniz ortasına pin atmak veya gece 03:00'te arama yapmak gibi), yanıt yine 200 OK döner, `Itineraries` boş liste (`[]`) olur ve `ReasonCode = "NO_ROUTE_FOUND"` döner.
- `Itineraries`: Bulunan en iyi alternatif rotaların listesi. (Sıralama algoritması tarafından hiyerarşik olarak dizilir).
- `Legs`: Bir Itinerary'nin alt adımlarıdır. "WALK" (Yürüme) ve "TRANSIT" (Toplu Taşıma) olmak üzere iki çeşidi vardır.

## 3. Hata (Error) Sözleşmesi

Beklenmeyen durumlar standart IETF RFC 7807 (ProblemDetails) formatında dönülür. Hiçbir zaman backend'in StackTrace bilgisi dışarıya açılmaz.

### 400 Bad Request
Gönderilen koordinatların menzil dışında olması (Türkiye dışı) veya tarih/zaman parametrelerinin geçersiz olması durumunda oluşur.
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Geçersiz arama parametreleri.",
  "status": 400,
  "detail": "Geçersiz arama parametreleri. Hata: Başlangıç noktası desteklenen menzil (Türkiye/Ege Bölgesi) dışındadır."
}
```

### 404 Not Found
Eğer sistemde aktif bir GTFS feed yoksa (sistem boşsa), rota aranmadan doğrudan bu hata verilir.
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Kullanılabilir aktif bir GTFS feed bulunamadı.",
  "status": 404,
  "detail": "Kullanılabilir aktif bir GTFS feed bulunamadı."
}
```

### 499 Client Closed Request (Operation Canceled)
Eğer rota arama çok uzun sürerse ve kullanıcı mobil cihazda / tarayıcıda aramayı iptal ederse (Cancellation Token), sunucu işlemi anında keser ve 499 döner.
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "İstek iptal edildi",
  "status": 499,
  "detail": "İstemci tarafında işlem iptal edildiği için sonuçlandırılamadı."
}
```

### 500 Internal Server Error
Beklenmeyen hatalarda (Veritabanı bağlantı kopması vs.) StackTrace gizlenerek standart bir hata mesajı dönülür.
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "Sunucu hatası",
  "status": 500,
  "detail": "Sunucu tarafında beklenmeyen bir hata oluştu."
}
```
