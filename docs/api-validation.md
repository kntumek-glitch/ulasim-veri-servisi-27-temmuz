# API Validation

## Test 1

**Test edilen endpoint**

GET https://openapi.izmir.bel.tr/api/iztek/duragayaklasanotobusler/10030

**Kullanýlan parametre**

- stopId: 10030

**HTTP durum kodu**

- 200 OK

**Response süresi**

- Örneðin: 150 ms

**Response boþ mu?**

- Hayýr

**Response içindeki alanlar**

- OtobusId
- HatNumarasi
- HatAdi
- KalanDurakSayisi
- HattinYonu
- KoorX
- KoorY
- EngelliMi
- BisikletAparatliMi

**Hata varsa hata mesajý**

- Yok

**Response yapýsý dokümanla uyumlu mu?**

- Evet




# API Validation

## Test 1

**Test Edilen Endpoint**
GET https://openapi.izmir.bel.tr/api/iztek/duragayaklasanotobusler/{durakId}

**DURAK ID**
10030

**HTTP Durum Kodu**
200 OK

**Gerçek Response Süresi**
41 ms

**Response Boþ mu?**
Hayýr

**Response Ýçinde Gelen Alanlar**
- KalanDurakSayisi
- HattinYonu
- KoorY
- BisikletAparatliMi
- KoorX
- EngelliMi
- HatNumarasi
- HatAdi
- OtobusId

**Response Örneði**

```json
{
  "KalanDurakSayisi": 1,
  "HatNumarasi": 304,
  "HatAdi": "TINAZTEPE-KONAK",
  "OtobusId": 11360
}
```

**Hata Mesajý**
Yok

**Dokümanla Uyumlu mu?**
Evet

---

## Test 2

**Test Edilen Endpoint**
GET https://openapi.izmir.bel.tr/api/iztek/duragayaklasanotobusler/{durakId}

**DURAK ID**
10019

**HTTP Durum Kodu**
200 OK

**Gerçek Response Süresi**
82.55 MS

**Response Boþ mu?**
Hayýr

**Response Ýçinde Gelen Alanlar**
- KalanDurakSayisi
- HattinYonu
- KoorY
- BisikletAparatliMi
- KoorX
- EngelliMi
- HatNumarasi
- HatAdi
- OtobusId

**Response Örneði**

```json
{
  "KalanDurakSayisi": 2,
  "HatNumarasi": 253,
  "HatAdi": "H.PINAR METRO - KONAK",
  "OtobusId": 2279
}
```

**Hata Mesajý**
Yok

**Dokümanla Uyumlu mu?**
Evet

---

## Test 3

**Test Edilen Endpoint**
GET https://openapi.izmir.bel.tr/api/iztek/duragayaklasanotobusler/{durakId}

**DURAK ID**
10107

**HTTP Durum Kodu**
200 OK

**Gerçek Response Süresi**
18.63 MS

**Response Boþ mu?**
Hayýr

**Response Ýçinde Gelen Alanlar**
- KalanDurakSayisi
- HattinYonu
- KoorY
- BisikletAparatliMi
- KoorX
- EngelliMi
- HatNumarasi
- HatAdi
- OtobusId

**Response Örneði**

```json
{
  "KalanDurakSayisi": 1,
  "HatNumarasi": 121,
  "HatAdi": "KONAK - MAVÝÞEHÝR AKTARMA MER.",
  "OtobusId": 12265
}
```

**Hata Mesajý**
Yok

**Dokümanla Uyumlu mu?**
Evet