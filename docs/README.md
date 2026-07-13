# Ulaþým Veri Servisi

## Projenin Amacý

Bu proje, Ýzmir Büyükþehir Belediyesi Açýk Veri Portalý ve ESHOT servislerinden alýnan ulaþým verilerini standart bir REST API üzerinden sunmak amacýyla geliþtirilmiþtir.

Proje kapsamýnda durak bilgileri, duraklardan geçen hatlar, yaklaþan otobüsler ve hat üzerindeki aktif araç konumlarý servis edilmektedir.

---

## Kullanýlan Teknolojiler

- ASP.NET Core Web API
- C#
- Entity Framework Core
- PostgreSQL
- Docker Compose
- Swagger / OpenAPI
- HttpClientFactory
- Memory Cache
- REST API

---

## Projeyi Çalýþtýrma

Projeyi Visual Studio 2022 ile açýn.

---

## Docker Compose ile PostgreSQL Baþlatma

```bash
docker compose up -d
```

---

## Migration Oluþturma

```powershell
Add-Migration InitialCreate
```

---

## Migration Uygulama

```powershell
Update-Database
```

---

## API'yi Çalýþtýrma

Visual Studio üzerinden **F5** veya **Ctrl + F5** ile projeyi çalýþtýrabilirsiniz.

---

## Swagger Adresi

```
https://localhost:7267/swagger
```

---

## CSV Import Endpointini Test Etme

```
POST /api/v1/import/stops
```

Swagger üzerinden **Execute** butonuna basýlarak test edilebilir.

---

## Stops Endpointlerini Test Etme

```
GET /api/v1/stops

GET /api/v1/stops/{id}

GET /api/v1/stops/by-external-id/{externalStopId}

GET /api/v1/stops/nearby
```

Bu endpointler Swagger üzerinden test edilebilir.

---

## Yeni Dýþ API Endpointlerini Test Etme

```
GET /api/v1/stops/{id}/approaching-buses

GET /api/v1/routes/{routeNumber}/vehicles
```

Bu endpointler ESHOT Açýk Veri servislerinden alýnan canlý verileri döndürmektedir.

---

## Cache Davranýþý

Yaklaþan otobüsler ve hat araç konumlarý istekleri **Memory Cache** kullanýlarak kýsa süreli önbelleðe alýnmaktadýr.

Cache süresi yaklaþýk **30 saniyedir**.

Response içerisinde bulunan **fromCache** alaný verinin önbellekten gelip gelmediðini göstermektedir.

---

## ExternalApiLogs

Dýþ API'ye yapýlan her istek veritabanýndaki **ExternalApiLogs** tablosuna kaydedilmektedir.

Kaydedilen bilgiler:

- EndpointName
- RequestUrl
- HttpStatusCode
- ResponseDurationMs
- IsSuccessful
- ErrorMessage
- CreatedAt