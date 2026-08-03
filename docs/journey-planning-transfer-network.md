# Kalıcı Transfer Ağı (Persistent Transfer Network)

Faz 5 ile birlikte, 2 aktarmalı rotaların (ve 1 aktarmalı rotaların daha hızlı) hesaplanabilmesi için "Kalıcı Transfer Ağı" yapısı kurulmuştur.

## 1. Neden Kalıcı Bir Ağa İhtiyacımız Var?
Önceki fazlarda, transfer olabilecek duraklar anlık olarak Haversine (mesafe) formülüyle runtime (çalışma zamanı) sırasında hesaplanıyordu. Bu durum:
- 1 aktarmalı rotalarda tolere edilebilir (O(N) karmaşıklık) iken,
- 2 aktarmalı rotalarda, birinci ve ikinci aktarma durakları arasındaki eşleşmelerin kartezyen çarpımını oluşturduğundan **O(N^2)** hatta **O(N^3)** seviyesine çıkarak sistem belleğini (RAM) tüketir ve veritabanını kilitler.

Bu sebeple, hangi durağın hangi durağa yürüme mesafesinde olduğu bilgisi önceden hesaplanıp (pre-calculate) `GtfsTransfers` tablosuna indekslenir. 

## 2. Mimari ve Algoritma
Transfer hesaplama mantığı `GtfsTransferCalculationService` içerisinde yer alır.

- **Spatial Grid (Izgara) Yaklaşımı:** Dünya, 500 metrelik hücrelere (Grid Cell) bölünür. Her durağın koordinatları bir Grid ID'ye (`lat / cellSize`, `lon / cellSize`) atanır.
- **Mesafe Filtresi:** Bir durağın etrafındaki durakları bulmak için sadece o hücredeki ve komşu 8 hücredeki duraklara Haversine formülü uygulanır. Tüm duraklar birbirleriyle karşılaştırılmaz.
- **MaxTransferWalkMeters:** Sadece aralarındaki mesafe yapılandırılabilir `MaxTransferWalkMeters` (varsayılan: 500m) altında olan durak eşleşmeleri ağa dahil edilir. Aynı pattern/rota üzerindeki aynı duraklar (A -> A) mantıksız olduğu için ağa eklenmez.
- **Deduplication:** A -> B ile B -> A transferleri çift yönlü olarak eklenir ancak algoritma esnasında gereksiz (aynı rota üzerinde ring yapan) transferler engellenir (GroupBy ve Distinct kontrolleri).

## 3. İzolasyon Mantığı (Run ID)
Sistemde aktif olarak çalışan bir GTFS verisi varken, arka planda yeni bir GTFS verisi içeri aktarılabilir (Zero-downtime). Bu yüzden transfer kayıtları globale değil, `GtfsImportRunId`'ye bağlıdır.
Yani her Import Run, kendi GTFS durak uzayını oluşturur ve kendi `GtfsTransfers` tablosunu indeksler. Aktif (Yayındaki) Run ID hangisiyse, Journey Planning algoritması o Run ID'nin transferlerini kullanır.

## 4. Yönetim Uç Noktaları (Admin Endpoints)
Bu süreci tetiklemek veya durumunu görmek için aşağıdaki Admin API'leri kullanılır. (İki endpoint de `X-Admin-Key` başlığı gerektirir).

### 4.1. Yeniden Oluşturma (Rebuild)
`POST /api/v1/admin/transfers/rebuild`
- **İşlev:** En son başarıyla tamamlanmış (Completed ve IsActive) Import Run'ı bulur ve o Run için var olan eski transferleri silip sıfırdan ağ oluşturur.
- **Not:** Bu işlem senkron çalışır ve birkaç saniye ile dakika arası sürebilir. CancellationToken desteklenir.

### 4.2. Durum Görüntüleme (Status)
`GET /api/v1/admin/transfers/status`
- **İşlev:** Aktif Run ID için tabloda kaç adet transfer bağlantısı bulunduğunu döndürür.
- **Örnek Yanıt:**
```json
{
  "activeRunId": 12,
  "totalTransfers": 45892,
  "lastUpdatedAt": "2026-08-03T10:15:30Z"
}
```
