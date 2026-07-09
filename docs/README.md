# Ulaþým Veri Servisi

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

Visual Studio üzerinden **F5** veya **Ctrl + F5** ile çalýþtýrabilirsiniz.

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