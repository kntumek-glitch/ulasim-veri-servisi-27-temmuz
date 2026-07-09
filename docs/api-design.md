# API Design

## POST /api/v1/import/stops

CSV dosyasýný okuyarak duraklarý PostgreSQL veritabanýna aktarýr.

Response

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

Duraklarý sayfalý olarak listeler.

Query Parametreleri

- search
- page
- pageSize

Response

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

Veritabanýndaki Id deðerine göre durak bilgisi döndürür.

---

## GET /api/v1/stops/by-external-id/{externalStopId}

Gerçek ESHOT durak numarasýna göre durak bilgisi döndürür.

---

## GET /api/v1/stops/nearby

Verilen koordinata yakýn duraklarý listeler.

Query Parametreleri

- latitude
- longitude
- radiusMeters

Response

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