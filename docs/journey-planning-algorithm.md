# Journey Planning Algoritması (Faz 5)

Bu doküman, Ulaşım Veri Servisi (Faz 5) kapsamında geliştirilen Yolculuk Planlama algoritmasının matematiksel ve mantıksal işleyişini açıklamaktadır. Algoritma temel olarak kullanıcının başlangıç noktasından (Origin) bitiş noktasına (Destination) giden en kısa (zamansal) rotayı; **"Sıfır Aktarma (Direkt)"**, **"1 Aktarmalı"** ve **"2 Aktarmalı"** olacak şekilde hesaplamak üzere dizayn edilmiştir.

## 1. Algoritma Aşamaları

Arama isteği (Search Request) geldiğinde sistem aşağıdaki adımları sırasıyla gerçekleştirir:

### Adım 1: Menzil Tespiti (Radius Search)
Kullanıcının koordinatlarına (Origin ve Destination) göre çevredeki duraklar aranır. Bu işlem, Spatial Grid indekslemesi (veya Haversine formülü) kullanılarak yapılır.
- Varsayılan yürüme mesafesi `1500 metredir` (Konfigürasyon destekli).
- Origin ve Destination etrafında maksimum `5` en yakın durak aday (Candidate Stop) olarak seçilir. (Fazla aday seçilmesi hesaplama maliyetini katlayacağından 5 ile sınırlandırılmıştır).

### Adım 2: Geçerli Servislerin (Takvim) Bulunması
Yolculuk gününün Pazartesi mi Pazar mı olduğuna göre GTFS takvim (`calendar.txt`) verisi filtrelenir. 
Ayrıca istisnai tatiller veya eklenen servisler (`calendar_dates.txt`) kontrol edilerek, o gün için geçerli olan (Aktif) servis ID'leri `activeServiceIds` listesinde toplanır.
> **Not (Gece Yarısı Geçişi):** Kullanıcı gece 02:00'de bir arama yapıyorsa, bu araç büyük ihtimalle bir önceki günün (Dünün) servisine aittir (Gece 26:00:00). Algoritma otomatik olarak saatin 04:00'ten erken olduğunu algılar ve "Dünün" aktif servislerini de aramaya dahil eder.

### Adım 3: 0-Transfer (Aktarmasız) Rotaların Bulunması
1. Origin etrafındaki duraklardan geçen `(DepartureSeconds > Kullanıcının Kalkış Saati + Yürüme Süresi)` olan tüm **Leg1 (İlk Bacak)** seferleri çekilir.
2. Bu seferlerden hangilerinin aynı zamanda `Destination` etrafındaki duraklardan da geçtiğine (ve `Destination.StopSequence > Origin.StopSequence` kuralına uyduğuna) bakılır.
3. Kurala uyanlar **Doğrudan Rota (Direct Route)** olarak kaydedilir.

### Adım 4: 1-Transfer (1 Aktarmalı) Rotaların Bulunması
1. 1-aktarmalı rotalar hesaplanırken, önceden indekslenmiş **Kalıcı Transfer Ağı (GtfsTransfers tablosu)** kullanılır.
2. Origin'den kalkan `Leg1` araçlarının indiği duraklar ile Destination'a giden `Leg2` araçlarının kalktığı duraklar arasındaki aktarma bağlantıları (maksimum 500 metre yürüme mesafesindeki duraklar arası transferler dahil) değerlendirilir.
3. **Zaman ve Tampon Kısıtlaması (Transfer Buffer):** `Leg2`'nin kalkış saati, `Leg1`'in iniş saatinden **en az 3 dakika** (Transfer Buffer) daha geç olmalıdır. Ayrıca maksimum bekleme süresini (örn. 60 dakika) aşmamalıdır. Eğe `Leg2` daha erken kalkıyorsa veya çok geç kalkıyorsa, rota reddedilir.
4. Kurala uyan birleşimler **Aktarmalı Rota (One Transfer Route)** olarak kaydedilir.

### Adım 5: 2-Transfer (2 Aktarmalı) Rotaların Bulunması
1. 2-aktarmalı rotalar, `Leg1 -> Transfer 1 -> Leg2 -> Transfer 2 -> Leg3` şeklinde işler.
2. 1. aktarma noktası ile 2. aktarma noktası arasındaki `Leg2` uçuşları taranırken yine **GtfsTransfers** kalıcı ağı kullanılır.
3. Performans için, `Leg1` ve `Leg3` rotalarındaki durak uzayları sınırlandırılır. Aksi halde O(N^3) karmaşıklığı RAM'i tüketir.
4. Hem Transfer 1 hem de Transfer 2 noktalarında **Transfer Buffer** (3 dk) kuralları işletilir.
5. **Pattern Deduplication (Ayıklama):** 2 aktarmalı rotalarda, art arda aynı hat/güzergah (pattern) üzerinden gereksiz in-bin yapılmasını önlemek için pattern kontrolleri yapılır. `pattern1 == pattern2` veya `pattern2 == pattern3` durumları elenir.

### Adım 6: Yürüme Adımlarının (Walk Legs) ve Ara Durakların Eklenmesi
Bulunan transit bacakların (TRANSIT) başına, sonuna ve aktarma noktalarına yürüme bacakları (WALK) eklenir. Kullanıcının hızı varsayılan olarak **1.4 metre/saniye** kabul edilir. İsteğe bağlı olarak (`includeIntermediateStops=true`) binilen ve inilen duraklar arasındaki tüm ara duraklar ve shapeId (harita çizimi) verisi yanıt modeline eklenir.

## 2. Sıralama (Sorting) Hiyerarşisi (Tie-Breakers)

Farklı opsiyonlar bulunduktan sonra kullanıcıya en mantıklı olanı (En üstte) sunmak için katı (Deterministik) bir hiyerarşi uygulanır. (Aşağıdaki sırayla ThenBy uygulanır)

1. **`ArrivalTime` (Varış Zamanı) [ASC]:** En erken ulaşan her zaman en iyisidir.
2. **`Transfers` (Aktarma Sayısı) [ASC]:** İki rota da 08:30'da varıyorsa, aktarmasız (0 transfer) olan, 1 aktarmalı olana tercih edilir.
3. **`TotalWalkingMeters` (Toplam Yürüme) [ASC]:** Eğer varış ve aktarma sayıları da eşitse, kullanıcıyı en az yürüten rota öne alınır.
4. **`TotalDurationMinutes` (Toplam Süre) [ASC]:** Yürümeler de eşitse, en az zaman alan (en geç kalkıp en erken varan) tercih edilir.
5. **`TotalTransitStops` (Durak Sayısı) [ASC]:** Süreler de eşitse, otobüsün/metronun içindeyken daha az durakta duran (Express/Hızlı) hat tercih edilir.
6. **`TripId` (Kimlik Bazlı Determinizm) [ASC]:** Tüm metrikler aynıysa, arama her yapıldığında sıralamanın değişmesini (Flaky order) engellemek için TripId alfabetik olarak sıralanır. (Kimlik bazlı tie-breaker).

## 3. Limitasyonlar ve Sınırlamalar
- Algoritma maksimum **2 Aktarmayı (2-Transfer)** destekler. 3 ve üzeri aktarmalar, PostGIS veya GraphDB (Neo4j, pgRouting) kullanılmadığı için RDBMS (PostgreSQL) üzerinde çok büyük performans sorunları yaratacağından kasten engellenmiştir.
- Transfer noktalarındaki yürüme mesafesi (`MaxTransferWalkMeters`) varsayılan olarak **500 metre** ile sınırlıdır (Config üzerinden değiştirilebilir).
- Gerçek zamanlı (GTFS-Realtime) GPS gecikme/trafik verisi olmadığı için tüm süreler planlanan statik saatlere göredir.
