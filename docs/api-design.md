# API Design

## POST /api/v1/import/stops

CSV dosyasýný okuyarak ESHOT durak verilerini PostgreSQL veritabanýna aktarýr.

### Response

```json
{
  "sourceName": "ESHOT Otobüs Duraklarý CSV",
  "importedRecordCount": 120,
  "updatedRecordCount": 5,
  "failedRecordCount": 0,
  "status": "Completed"
}
```

---

## GET /api/v1/stops

Duraklarý sayfalama ve arama desteðiyle listeler.

### Query Parametreleri

- search
- page
- pageSize

### Response

```json
{
  "items": [
    {
      "id": 1,
      "externalStopId": "10030",
      "name": "Durak Adý",
      "latitude": 38.389985,
      "longitude": 27.16657,
      "routes": [
        "304",
        "465"
      ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 120
}
```

---

## GET /api/v1/stops/{id}

Veritabanýndaki durak Id'sine göre durak bilgisini döndürür.

### Response

```json
{
  "id": 1,
  "externalStopId": "10030",
  "name": "Durak Adý",
  "latitude": 38.389985,
  "longitude": 27.16657,
  "routes": [
    "304",
    "465"
  ]
}
```

---

## GET /api/v1/stops/by-external-id/{externalStopId}

ESHOT durak numarasýna göre durak bilgisini döndürür.

### Response

```json
{
  "id": 1,
  "externalStopId": "10030",
  "name": "Durak Adý",
  "latitude": 38.389985,
  "longitude": 27.16657,
  "routes": [
    "304",
    "465"
  ]
}
```

---

## GET /api/v1/stops/nearby

Verilen koordinata belirli bir yarýçap içerisindeki duraklarý listeler.

### Query Parametreleri

- latitude
- longitude
- radiusMeters

### Response

```json
{
  "items": [
    {
      "id": 1,
      "externalStopId": "10030",
      "name": "Durak Adý",
      "latitude": 38.389985,
      "longitude": 27.16657,
      "distanceMeters": 350
    }
  ]
}
```

---

## GET /api/v1/stops/{id}/approaching-buses

Belirtilen duraða yaklaþan otobüsleri ESHOT servisinden getirir.

### Response

```json
{
  "stopId": 10107,
  "externalStopId": "50278",
  "retrievedAt": "2026-07-10T07:52:21Z",
  "fromCache": false,
  "buses": [
    {
      "busId": "11535",
      "routeNumber": "7",
      "routeName": "SAHÝLEVLERÝ - ÜÇKUYULAR ÝSK.",
      "remainingStopCount": 1,
      "direction": "1",
      "latitude": 38.412025,
      "longitude": 27.01735167,
      "isAccessible": true,
      "hasBicycleRack": false
    }
  ]
}
```

---

## GET /api/v1/routes/{routeNumber}/vehicles

Belirtilen hatta çalýþan aktif araçlarýn konumlarýný ESHOT servisinden getirir.

### Response

```json
{
  "routeNumber": "304",
  "retrievedAt": "2026-07-10T10:26:33Z",
  "fromCache": false,
  "vehicles": [
    {
      "busId": "2004",
      "direction": "1",
      "latitude": 38.38643167,
      "longitude": 27.18797333
    }
  ]
}
```