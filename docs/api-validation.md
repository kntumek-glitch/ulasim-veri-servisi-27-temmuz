# API Validation

## Swagger Testleri

Swagger üzerinden aþaðýdaki endpointler baþarýyla test edilmiþtir.

---

## 1. Durak Listeleme

**Endpoint**

```
GET /api/v1/stops
```

**Sonuç**

- Status Code: 200 OK
- Durak listesi baþarýyla döndü.
- Sayfalama ve arama desteði doðrulandý.

---

## 2. Durak Detayý

**Endpoint**

```
GET /api/v1/stops/{id}
```

**Örnek**

```
GET /api/v1/stops/1
```

**Sonuç**

- Status Code: 200 OK
- Durak bilgileri baþarýyla getirildi.

---

## 3. Yakýndaki Duraklar

**Endpoint**

```
GET /api/v1/stops/nearby
```

**Test Parametreleri**

```
latitude=38.42
longitude=27.13
radiusMeters=500
```

```
latitude=38.42
longitude=27.13
radiusMeters=1000
```

```
latitude=38.42
longitude=27.13
radiusMeters=2000
```

**Sonuç**

- Status Code: 200 OK
- Yakýndaki duraklar baþarýyla listelendi.
- Response içerisinde `distanceMeters` alaný doðrulandý.
- Farklý yarýçap deðerlerinde beklenen þekilde farklý sayýda sonuç döndü.

---

## 4. CSV Import

**Endpoint**

```
POST /api/v1/import/stops
```

**Sonuç**

- Status Code: 200 OK
- CSV verileri baþarýyla aktarýldý.
- Stops ve StopRoutes tablolarý güncellendi.
- ImportRun kaydý oluþturuldu.

---

## 5. Yaklaþan Otobüsler

**Endpoint**

```
GET /api/v1/stops/{id}/approaching-buses
```

**Sonuç**

- Status Code: 200 OK
- Dýþ API üzerinden yaklaþan otobüs bilgileri baþarýyla alýndý.
- Koordinat dönüþümleri doðru þekilde gerçekleþtirildi.
- Baþarýlý istek ExternalApiLogs tablosuna kaydedildi.

---

## 6. Hat Araç Konumlarý

**Endpoint**

```
GET /api/v1/routes/{routeNumber}/vehicles
```

**Örnek**

```
GET /api/v1/routes/304/vehicles
```

**Sonuç**

- Status Code: 200 OK
- Aktif araç konumlarý baþarýyla getirildi.
- Araç koordinatlarý doðru þekilde dönüþtürüldü.
- Baþarýlý istek ExternalApiLogs tablosuna kaydedildi.

---

## 7. Memory Cache

**Test**

Ayný endpoint kýsa süre içerisinde art arda çaðrýldý.

**Sonuç**

- Cache mekanizmasý baþarýyla çalýþtý.
- Response içerisindeki `fromCache` alaný cache kullanýmýný gösterdi.

---

## Genel Sonuç

Projede geliþtirilen tüm endpointler Swagger üzerinden test edilmiþ ve beklenen sonuçlar doðrulanmýþtýr.