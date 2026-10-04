# Ulaşım Veri Servisi (Transportation Data Service)

![.NET](https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white)
![React](https://img.shields.io/badge/React-20232A?style=for-the-badge&logo=react&logoColor=61DAFB)
![Postgres](https://img.shields.io/badge/postgres-%23316192.svg?style=for-the-badge&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/docker-%230db7ed.svg?style=for-the-badge&logo=docker&logoColor=white)

Bu proje, İzmir (ESHOT vb.) ulaşım verilerini, GTFS formatlı veritabanı senkronizasyonunu ve canlı veri akışını yöneten merkezi backend servisidir.

## 🚀 Hızlı Başlangıç (Quick Start)

Projeyi kendi bilgisayarınızda çalıştırmak için Docker'ın kurulu olduğundan emin olun.

1. Tüm backend, veritabanı ve ulaşım (OSRM/OTP) servislerini Docker üzerinden ayağa kaldırın:
   ```bash
   docker-compose up --build -d
   ```

2. Frontend (Web UI) arayüzünü başlatın:
   ```bash
   npm run dev
   ```

Servisler ayağa kalktığında API dokümantasyonuna `http://localhost:5108/swagger` adresinden, React arayüzüne ise terminalde belirtilen adresten (genelde `http://localhost:5173`) ulaşabilirsiniz.

---

## 🏗️ Sistem Mimarisi (Phase 7 - V2)

Projenin ulaşım motoru (Journey Planning) mimarisi aşağıdaki 5 temel bileşen etrafında şekillenmiştir:

1. **Static GTFS Data Source (Statik GTFS Veri Kaynağı):**
   Toplu taşıma ağının durağan verilerini (duraklar, rotalar, sefer saatleri, takvim istisnaları) içeren veritabanıdır. Dinamik gecikmeleri (GTFS-RT) içermez. Tüm veriler periyodik import işlemleriyle senkronize edilir.

2. **V1 Legacy Journey Planner (Eski Nesil Rotalama Motoru):**
   Eski Graph ve SQL tabanlı, Dijkstra/A* algoritmasına dayanan V1 arama motorudur. Veritabanına yoğun yük bindirdiği ve çoklu aktarmalarda yavaş kaldığı için yerini V2'ye bırakmıştır. Geçmişe dönük uyumluluk için `/api/v1/journey-plans/search` endpoint'i altında halen korunmaktadır.

3. **RoutingSnapshot (In-Memory Veri Ağı):**
   Performans darboğazlarını aşmak için GTFS veritabanının RAM'e (In-Memory) optimize edilmiş, salt okunur bir kopyasıdır. Aramalar SQL sorgusu çalıştırmadan tamamen bu RAM kopyası üzerinden Array indeksleri ile mikrosaniyeler içinde yapılır. Sıfır kesinti (Zero Downtime) ile güncellenir.

4. **V2 RAPTOR Engine (Yeni Nesil Rotalama Motoru):**
   Round-based (Tur-tabanlı) Connection Scan (RAPTOR) algoritmasını kullanan yüksek performanslı motordur. Node ve Edge mantığını bırakarak sefer-desenleri (trip patterns) üzerinde çalışır. Çoklu aktarma senaryolarında eski sisteme göre 100 kattan daha hızlı sonuç üretir. `/api/v2/journey-plans/search` endpoint'i üzerinden hizmet verir.

5. **DEPART_AT vs ARRIVE_BY (Yönlü Arama Modları):**
   - `DEPART_AT`: Belirtilen bir saatten **itibaren** yola çıkıp en erken (en hızlı) varışı hedefler (Zaman ekseninde ileriye tarama).
   - `ARRIVE_BY`: Belirtilen saate **kadar** hedefte olabilmek için evden çıkılması gereken en geç (maksimum) saati hesaplar (Zaman ekseninde geriye doğru izole tarama).

## 🛡️ Güvenlik ve Kurulum Kuralları
- **Admin API Key:** Veritabanını mutasyona uğratan Import, Reconcile ve Rebuild Transfers endpoint'leri `X-Admin-Key` gerektirir. Bu anahtarı hiçbir zaman koda yazmayınız, Docker `.env` dosyası üzerinden `AdminSettings__ApiKey` env variable ile sunucuya veriniz.

## 📡 Endpoint Listesi ve İletişim
Servisin tam Swagger dokümantasyonu geliştirme (Development) ortamında `/swagger` yolu üzerinden otomatik ayağa kalkar. Örnek kullanım ve testler için test/integration dizinine bakabilir, detaylı algoritma açıklamaları için `docs/` klasöründeki belgelere göz atabilirsiniz.
