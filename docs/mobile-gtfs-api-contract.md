# Mobil GTFS API Kontratı

Bu doküman, mobil uygulamaların (veya diğer istemcilerin) Ulaşım Veri Servisi'nden veri çekerken kullanacakları JSON veri modellerini (DTO'ları) tanımlamaktadır. API'den dönen gerçek yanıtlarla birebir eşleşmektedir.

## 1. Departures Endpoint (Kalkış Saatleri)
**GET** `/api/v1/gtfs/routes/{routeId}/departures`

Belirli bir hatta, belirli bir yönde ve tarihteki tüm sefer kalkış saatlerini (ve tahmini/canlı bilgileri) döner.

### Response
```json
{
  "data": [
    {
      "tripId": "1001-1-D",
      "directionId": 0,
      "headsign": "KARŞIYAKA İSKELE",
      "departureTime": "08:15:00",
      "departureSeconds": 29700,
      "serviceId": "Haftaİçi",
      "calendarValidity": "2024-01-01 - 2024-12-31",
      "isFeedStale": false
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalRecords": 1
  },
  "metadata": {
    "isFeedExpired": false,
    "missingCalendarDatesFile": false
  }
}
```

## 2. Pattern Endpoint (Sefer Durakları / Tripler)
**GET** `/api/v1/gtfs/trips/{tripId}/stops`

Belirli bir sefere (trip) ait güzergâhtaki durakların varış/kalkış saatlerini sırasıyla döner.

### Response
```json
{
  "tripId": "1001-1-D",
  "routeId": "304",
  "directionId": 0,
  "serviceId": "Haftaİçi",
  "headsign": "TINAZTEPE",
  "shapeId": "shp-304-0",
  "stops": [
    {
      "stopId": "10030",
      "stopName": "Tınaztepe",
      "stopSequence": 1,
      "arrivalTime": "08:15:00",
      "arrivalTimeSeconds": 29700,
      "departureTime": "08:15:00",
      "departureTimeSeconds": 29700
    }
  ]
}
```

## 3. Route Patterns Endpoint (Hattın Güzargâh Desenleri)
**GET** `/api/v1/gtfs/routes/{routeId}/patterns`

Belirli bir hattın (route) tüm farklı varyasyonlarını (pattern) ve temsilci Trip bilgisini döner.

### Response
```json
[
  {
    "patternId": "TINAZTEPE-KARSIYAKA-0",
    "routeId": "304",
    "directionId": 0,
    "representativeTripId": "1001-1-D",
    "shapeId": "shp-304-0",
    "tripCount": 45,
    "stopCount": 32,
    "startStop": {
      "stopId": "10030",
      "stopCode": "10030",
      "stopName": "Tınaztepe",
      "latitude": 38.389985,
      "longitude": 27.16657,
      "platformCode": null
    },
    "endStop": {
      "stopId": "10045",
      "stopCode": "10045",
      "stopName": "Karşıyaka İskele",
      "latitude": 38.45501,
      "longitude": 27.11899,
      "platformCode": null
    }
  }
]
```

## 4. GeoJSON Shape Endpoint (Güzargâh Çizgisi)
**GET** `/api/v1/gtfs/shapes` (Query parameters: `tripId`, `patternId` veya `shapeId`)

Harita üzerinde otobüsün geçeceği güzergâhın koordinat dizisini döner. 
Doğrudan GeoJSON yerine, metadata ve GeoJSON (Feature/LineString) wrapper'ı içeren standartlaştırılmış bir model döner.

### Response
```json
{
  "shapeId": "shp-304-0",
  "tripId": "1001-1-D",
  "patternId": "TINAZTEPE-KARSIYAKA-0",
  "coordinates": [
    {
      "lat": 38.389985,
      "lon": 27.16657,
      "sequence": 1
    }
  ],
  "geoJson": {
    "type": "Feature",
    "geometry": {
      "type": "LineString",
      "coordinates": [
        [27.16657, 38.389985]
      ]
    }
  }
}
```
