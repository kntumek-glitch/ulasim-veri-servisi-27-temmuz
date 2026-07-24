# Database Design

## Stop

Otobüs duraklarını tutar.

| Alan | Tip |
|------|-----|
| Id | int |
| ExternalStopId | string |
| Name | string |
| Latitude | double |
| Longitude | double |
| CreatedAt | DateTime |
| UpdatedAt | DateTime |

---

## StopRoute

Bir durağın geçtiği hatları tutar.

| Alan | Tip |
|------|-----|
| Id | int |
| StopId | int |
| RouteNumber | string |
| CreatedAt | DateTime |

### İlişki

- Stop (1) → (N) StopRoute

---

## GtfsImportRun

GTFS import işlemlerinin geçmişini tutar.

### Saklanan Bilgiler

- Dosya adı
- SHA-256 hash değeri
- Başlangıç zamanı
- Bitiş zamanı
- Durum
- Hata mesajı (varsa)

### Amaç

- Aynı GTFS dosyasının tekrar içe aktarılmasını engellemek.
- İçe aktarma geçmişini kayıt altında tutmak.
- Hata durumlarını izleyebilmek.

## ExternalApiLog

Dış API servislerine yapılan istekleri kayıt altına alır.

| Alan | Tip |
|------|-----|
| Id | int |
| EndpointName | string |
| RequestUrl | string |
| HttpStatusCode | int |
| ResponseDurationMs | int |
| IsSuccessful | bool |
| ErrorMessage | string |
| CreatedAt | DateTime |

---
---

# GTFS Tabloları

## GtfsAgency

GTFS veri setindeki işletmeci (agency) bilgilerini tutar.

### Index

- AgencyId (Unique)

---

## GtfsRoute

GTFS hat (route) bilgilerini tutar.

### İlişkiler

- GtfsRoute (1) → (N) GtfsTrip


### Indexler

| Alan | Amaç |
|------|------|
| RouteId | Hat sorgularını hızlandırır. |

---

## GtfsStop

GTFS durak bilgilerini tutar.

### İlişkiler

- GtfsStop (1) → (N) GtfsStopTime


### Indexler

| Alan | Amaç |
|------|------|
| StopId | Durak sorgularını hızlandırır. |

---

## GtfsTrip

Hatlara ait sefer (trip) bilgilerini tutar.

### İlişkiler

- GtfsRoute (1) → (N) GtfsTrip
- GtfsTrip (1) → (N) GtfsStopTime


### Indexler

| Alan | Amaç |
|------|------|
| TripId | Sefer sorgularını hızlandırır. |
| RouteId | Hat bazlı sorgular için kullanılır. |

---

## GtfsStopTime

Bir seferin hangi durağa hangi sırada ve hangi saatte uğradığını tutar.

### İlişkiler

- GtfsTrip (N) → (1) GtfsTrip
- GtfsStop (N) → (1) GtfsStop

---

## GtfsCalendar

Servislerin haftalık çalışma günlerini tutar.

---

## GtfsCalendarDate

Takvim istisnalarını (eklenen veya iptal edilen servis günleri) tutar.

---

## GtfsShapePoint

Hat geometrisini oluşturan koordinat noktalarını tutar.

---

## GtfsImportRun

GTFS import işlemlerinin geçmişini tutar.

### Index

- FileHash

## Veritabanı İlişkileri

### Eski Veri Modeli

- Stop (1) → (N) StopRoute
- ImportRun tablosu CSV import işlemlerini kayıt altına alır.
- ExternalApiLog tablosu dış API çağrılarını kayıt altına alır.

### GTFS Veri Modeli

- GtfsRoute (1) → (N) GtfsTrip
- GtfsTrip (1) → (N) GtfsStopTime
- GtfsStop (1) → (N) GtfsStopTime

GtfsStopTime tablosu, GtfsTrip ve GtfsStop tabloları arasında bağlantı kurarak bir seferin hangi durağa hangi sırayla uğradığını temsil eder.

## Veritabanı Yapısı

Proje iki farklı veri modeli içermektedir:

### 1. CSV Tabanlı Model

İlk geliştirme aşamasında kullanılan tablolar:

- Stop
- StopRoute
- ImportRun
- ExternalApiLog

### 2. GTFS Veri Modeli

GTFS dosyalarının içe aktarılmasıyla oluşturulan tablolar:

- GtfsAgency
- GtfsRoute
- GtfsStop
- GtfsTrip
- GtfsStopTime
- GtfsCalendar
- GtfsCalendarDate
- GtfsShapePoint
- GtfsImportRun

Veritabanı tasarımının ayrıntıları `docs/database-design.md` dosyasında açıklanmıştır.

# ER Diyagramı (Özet)

GtfsAgency
      │
      │
      ▼
GtfsRoute
      │
      │ 1 - N
      ▼
GtfsTrip
      │
      │ 1 - N
      ▼
GtfsStopTime
      ▲
      │ N - 1
GtfsStop


GtfsCalendar
      │
      └──────► GtfsTrip

GtfsCalendarDate
      │
      └──────► GtfsCalendar

      ## GTFS İlişkileri

| Kaynak Tablo | İlişkisi | Hedef Tablo |
|--------------|----------|-------------|
| GtfsRoute | 1 → N | GtfsTrip |
| GtfsTrip | 1 → N | GtfsStopTime |
| GtfsStop | 1 → N | GtfsStopTime |
| GtfsCalendar | 1 → N | GtfsCalendarDate |