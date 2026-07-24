# GTFS Analysis

## Genel Bilgiler

GTFS ZIP dosyasý incelenmiþtir.

Kaynak:

https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip

---

## agency.txt

| Özellik | Deðer |
|---------|-------|
| Dosya mevcut | Evet |
| Satýr sayýsý | 1 |
| Kolonlar | agency_id, agency_name, agency_url, agency_timezone, agency_phone, agency_lang |
| Zorunlu alanlarda boþ deðer | 0 |
| Tekrarlayan anahtar | 0 |
| Parse edilemeyen kayýt | 0 |

Kullaným amacý:

- Kurum bilgileri
- Operatör adý
- Zaman dilimi
- Ýletiþim bilgileri

---

## routes.txt

| Özellik | Deðer |
|---------|-------|
| Dosya mevcut | Evet |
| Satýr sayýsý | 427 |
| Kolonlar | route_id, route_short_name, route_long_name, route_type |
| Zorunlu alanlarda boþ deðer | 0 |
| Tekrarlayan anahtar | 0 |
| Parse edilemeyen kayýt | 0 |

Kullaným amacý:

- Hat listesi
- Hat numarasý
- Hat adý
- Hat tipi

---

## stops.txt

| Özellik | Deðer |
|---------|-------|
| Dosya mevcut | Evet |
| Satýr sayýsý | 11 510 |
| Kolonlar | stop_id, stop_name, stop_lat, stop_lon |
| Zorunlu alanlarda boþ deðer | 0 |
| Tekrarlayan anahtar | 0 |
| Parse edilemeyen kayýt | 0 |

Kullaným amacý:

- Durak bilgileri
- Durak koordinatlarý
- Durak isimleri

---

## trips.txt

| Özellik | Deðer |
|---------|-------|
| Dosya mevcut | Evet |
| Satýr sayýsý | 65 012 |
| Kolonlar | route_id, service_id, trip_id, direction_id, wheelchair_accessible, bikes_allowed, shape_id |
| Zorunlu alanlarda boþ deðer | 0 |
| Tekrarlayan anahtar | 0 |
| Parse edilemeyen kayýt | 0 |

Kullaným amacý:

- Seferler
- Hat yönü
- Shape iliþkileri
- Servis iliþkileri

---

## stop_times.txt

| Özellik | Deðer |
|---------|-------|
| Dosya mevcut | Evet |
| Satýr sayýsý | 2 216 478 |
| Kolonlar | trip_id, arrival_time, departure_time, stop_id, stop_sequence, timepoint |
| Zorunlu alanlarda boþ deðer | 0 |
| Tekrarlayan anahtar | 0 |
| Parse edilemeyen kayýt | 0 |

Kullaným amacý:

- Durak sýralarý
- Varýþ saatleri
- Kalkýþ saatleri
- Sefer akýþý

---

## calendar.txt

| Özellik | Deðer |
|---------|-------|
| Dosya mevcut | Evet |
| Satýr sayýsý | 3 |
| Kolonlar | service_id, monday, tuesday, wednesday, thursday, friday, saturday, sunday, start_date, end_date |
| Zorunlu alanlarda boþ deðer | 0 |
| Tekrarlayan anahtar | 0 |
| Parse edilemeyen kayýt | 0 |

Kullaným amacý:

- Servis günleri
- Geçerlilik tarihleri
- Çalýþma takvimi

---

## shapes.txt

| Özellik | Deðer |
|---------|-------|
| Dosya mevcut | Evet |
| Satýr sayýsý | 404 229 |
| Kolonlar | shape_id, shape_pt_lat, shape_pt_lon, shape_pt_sequence |
| Zorunlu alanlarda boþ deðer | 0 |
| Tekrarlayan anahtar | 0 |
| Parse edilemeyen kayýt | 0 |

Kullaným amacý:

- Hat geometrisi
- Harita üzerinde güzergâh çizimi

---

# Eksik Dosyalar

Bu GTFS paketinde aþaðýdaki standart GTFS dosyalarý bulunmamaktadýr.

- calendar_dates.txt
- feed_info.txt

Bu nedenle servis istisnalarý ve feed sürüm bilgileri bu veri setinden elde edilememektedir.