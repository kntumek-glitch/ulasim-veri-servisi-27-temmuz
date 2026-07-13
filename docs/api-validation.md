# API Validation

## Swagger Testleri

Swagger üzerinden aþaðýdaki endpointler test edilmiþtir.

---

# 1. Durak Listeleme

Endpoint:

GET /api/v1/stops


Sonuç:

- Status Code: 200 OK
- Durak listesi baþarýyla döndü.

---

# 2. Durak Detay

Endpoint:

GET /api/v1/stops/{id}


Örnek:

GET /api/v1/stops/1


Sonuç:

- Status Code: 200 OK
- Durak bilgileri baþarýyla getirildi.

---

# 3. Yakýndaki Duraklar

Endpoint:

GET /api/v1/stops/nearby


Parametreler:

latitude

longitude

radiusMeters


Sonuç:

- Status Code: 200 OK
- Konuma yakýn duraklar listelendi.

---

# 4. CSV Import

Endpoint:

POST /api/v1/import/stops


Sonuç:

- CSV verileri baþarýyla aktarýldý.
- Stops ve StopRoutes tablolarý güncellendi.

---

# 5. Yaklaþan Otobüsler

Endpoint:

GET /api/v1/stops/{id}/approaching-buses


Sonuç:

- Dýþ API üzerinden otobüs bilgileri baþarýyla alýndý.