# Journey Planning API Contract (Faz 5)

Bu doküman, Ulaşım Veri Servisi (Faz 5) kapsamında geliştirilen `/api/v1/journey-plans/search` endpoint'inin İstek (Request) ve Yanıt (Response) yapısını açıklar.

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
  "maxTransfers": 2,
  "maxWalkingMeters": 1500,
  "maxResults": 10,
  "includeIntermediateStops": true
}
```

#### Parametreler
- `origin` ve `destination`: Başlangıç ve varış noktalarının enlem ve boylam değerleri. (Zorunlu)
- `departureDateTime`: İstenen başlangıç tarihi ve saati. `DateTimeOffset` (ISO 8601) formatında olmalıdır. (Zorunlu)
- `maxTransfers`: Rota üzerindeki maksimum aktarma sayısı. Sistem `0, 1 ve 2` aktarmalı rotaları desteklemektedir. Default: 1.
- `maxWalkingMeters`: Kullanıcının katlanabileceği maksimum (toplam) yürüme mesafesi (metre). Opsiyoneldir, default: 1500.
- `maxResults`: Döndürülecek maksimum rota (itinerary) sayısı. Opsiyoneldir, default: 10.
- `includeIntermediateStops`: Eğer true gönderilirse transit bacaklar (legs) içerisine geçilen ara duraklar (IntermediateStops) listesi eklenir. Varsayılanı false'tur.

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
      "dataSource": "STATIC_GTFS",
      "routeTypeSummary": "Bus",
      "departureTime": "2024-01-01T08:00:00+03:00",
      "arrivalTime": "2024-01-01T08:38:00+03:00",
      "totalDurationMinutes": 38,
      "totalJourneyTimeSeconds": 2280,
      "transferCount": 0,
      "totalWalkingDistanceMeters": 700,
      "totalWalkingTimeSeconds": 480,
      "totalWaitingTimeSeconds": 240,
      "totalInVehicleTimeSeconds": 1800,
      "initialWaitTimeSeconds": 240,
      "transferWaitTimes": [],
      "serviceDate": "2024-01-01",
      "totalTransitStopCount": 5,
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
          "routeType": 3,
          "patternId": "P1",
          "shapeId": "SH1",
          "routeId": "R1",
          "routeShortName": "100",
          "tripId": "T1",
          "directionId": 0,
          "headsign": "Dest",
          "serviceId": "SV1",
          "serviceDate": "2024-01-01",
          "fromStopId": "S1",
          "fromStopName": "Origin",
          "fromStopSequence": 1,
          "toStopId": "S3",
          "toStopName": "Dest",
          "toStopSequence": 6,
          "rawGtfsDepartureTime": "08:04:00",
          "rawGtfsDepartureSeconds": 29040,
          "departureTime": "2024-01-01T08:04:00+03:00",
          "rawGtfsArrivalTime": "08:34:00",
          "rawGtfsArrivalSeconds": 30840,
          "arrivalTime": "2024-01-01T08:34:00+03:00",
          "intermediateStopCount": 4,
          "distanceMeters": 0,
          "durationMinutes": 30,
          "stopCount": 5,
          "intermediateStops": [
            {
              "stopId": "S2",
              "stopCode": "ST2",
              "stopName": "Intermediate Stop",
              "stopSequence": 2,
              "rawGtfsArrivalTime": "08:10:00",
              "rawGtfsDepartureTime": "08:10:00",
              "arrivalSeconds": 29400,
              "departureSeconds": 29400,
              "arrivalTime": "2024-01-01T08:10:00+03:00",
              "lat": 38.405,
              "lon": 27.105
            }
          ]
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

#### Parametreler (Faz 5 ile Gelen Yenilikler)
- `includeIntermediateStops`: Client'ın `true` göndermesi durumunda her `TRANSIT` bacağı altında `intermediateStops` dizisi (Array) döner.
- `patternId` ve `shapeId`: Harita üzerine rota çizimi yaparken (Shape Points) çekmek için gereken benzersiz kimlikler.
- `TotalJourneyTimeSeconds`, `TotalInVehicleTimeSeconds`, `TotalWaitingTimeSeconds` gibi metrikler ile UI'da çok daha detaylı istatistikler ve "süre grafik barları" gösterilebilir.

## 3. Hata (Error) Sözleşmesi

Beklenmeyen durumlar standart IETF RFC 7807 (ProblemDetails) formatında dönülür. Hiçbir zaman backend'in StackTrace bilgisi dışarıya açılmaz.
Bağlantı koptuğunda (Cancellation Token) veya istemci işlemi iptal ettiğinde sunucu `499 Client Closed Request` hatası dönmektedir.
