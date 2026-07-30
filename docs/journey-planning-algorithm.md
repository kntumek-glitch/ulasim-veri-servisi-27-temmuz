# Journey Planning Algoritması (Faz 4)

Bu doküman, Ulaşım Veri Servisi (Faz 4) kapsamında geliştirilen Yolculuk Planlama algoritmasının matematiksel ve mantıksal işleyişini açıklamaktadır. Algoritma temel olarak kullanıcının başlangıç noktasından (Origin) bitiş noktasına (Destination) giden en kısa (zamansal) rotayı; **"Sıfır Aktarma (Direkt)"** ve **"En Fazla Bir Aktarma"** olacak şekilde hesaplamak üzere dizayn edilmiştir.

## 1. Algoritma Aşamaları

Arama isteği (Search Request) geldiğinde sistem aşağıdaki adımları sırasıyla gerçekleştirir:

### Adım 1: Menzil Tespiti (Radius Search)
Kullanıcının koordinatlarına (Origin ve Destination) göre çevredeki duraklar aranır. Bu işlem, `CoordinateHelper.CalculateDistance` (Haversine formülü) kullanılarak yapılır.
- Varsayılan yürüme mesafesi `1500 metredir` (Konfigürasyon destekli).
- Origin ve Destination etrafında maksimum `5` en yakın durak aday (Candidate Stop) olarak seçilir. (Fazla aday seçilmesi N+1 sorgu maliyetini katlayacağından 5 ile sınırlandırılmıştır).

### Adım 2: Geçerli Servislerin (Takvim) Bulunması
Yolculuk gününün Pazartesi mi Pazar mı olduğuna göre GTFS takvim (`calendar.txt`) verisi filtrelenir. 
Ayrıca istisnai tatiller veya eklenen servisler (`calendar_dates.txt`) kontrol edilerek, o gün için geçerli olan (Aktif) servis ID'leri `activeServiceIds` listesinde toplanır.
> **Not (Gece Yarısı Geçişi):** Kullanıcı gece 02:00'de bir arama yapıyorsa, bu araç büyük ihtimalle bir önceki günün (Dünün) servisine aittir (Gece 26:00:00). Algoritma otomatik olarak saatin 04:00'ten erken olduğunu algılar ve "Dünün" aktif servislerini de aramaya dahil eder.

### Adım 3: 0-Transfer (Aktarmasız) Rotaların Bulunması
1. Origin etrafındaki duraklardan geçen `(DepartureSeconds > Kullanıcının Kalkış Saati + Yürüme Süresi)` olan tüm **Leg1 (İlk Bacak)** seferleri çekilir.
2. Bu seferlerden hangilerinin aynı zamanda `Destination` etrafındaki duraklardan da geçtiğine (ve `Destination.StopSequence > Origin.StopSequence` kuralına uyduğuna) bakılır.
3. Kurala uyanlar **Doğrudan Rota (Direct Route)** olarak kaydedilir.

### Adım 4: 1-Transfer (Maksimum 1 Aktarmalı) Rotaların Bulunması
1. Kullanıcının binebileceği tüm ilk araçların (`Leg1`) inebileceği (Destination OLMAYAN) diğer tüm duraklar (Transfer Noktaları) keşfedilir.
2. Bu transfer noktalarından, `Destination` aday duraklarına giden `Leg2` (İkinci Bacak) seferleri bulunur.
3. **Zaman ve Tampon Kısıtlaması (Transfer Buffer):** `Leg2`'nin kalkış saati, `Leg1`'in iniş saatinden **en az 3 dakika** (Transfer Buffer) daha geç olmalıdır. Bu kural, yolcunun aktarma yaparken otobüsü kaçırmaması için güvenlik payıdır. Eğer `Leg2` daha erken kalkıyorsa, rota reddedilir.
4. Kurala uyan birleşimler **Aktarmalı Rota (Transfer Route)** olarak kaydedilir.

### Adım 5: Yürüme Adımlarının (Walk Legs) Eklenmesi
Bulunan transit bacakların (TRANSIT) başına ve sonuna yürüme bacakları (WALK) eklenir. Kullanıcının hızı varsayılan olarak **1.4 metre/saniye** kabul edilir. Toplam seyahat süresi (Duration), yürüme süreleri dahil edilerek kesinleştirilir.

## 2. Sıralama (Sorting) Hiyerarşisi (Tie-Breakers)

Farklı opsiyonlar bulunduktan sonra kullanıcıya en mantıklı olanı (En üstte) sunmak için katı (Deterministik) bir hiyerarşi uygulanır. (Aşağıdaki sırayla ThenBy uygulanır)

1. **`ArrivalTime` (Varış Zamanı) [ASC]:** En erken ulaşan her zaman en iyisidir.
2. **`Transfers` (Aktarma Sayısı) [ASC]:** İki rota da 08:30'da varıyorsa, aktarmasız (0 transfer) olan, 1 aktarmalı olana tercih edilir.
3. **`TotalWalkingMeters` (Toplam Yürüme) [ASC]:** Eğer varış ve aktarma sayıları da eşitse, kullanıcıyı en az yürüten rota öne alınır.
4. **`TotalDurationMinutes` (Toplam Süre) [ASC]:** Yürümeler de eşitse, en az zaman alan (en geç kalkıp en erken varan) tercih edilir.
5. **`TotalTransitStops` (Durak Sayısı) [ASC]:** Süreler de eşitse, otobüsün/metronun içindeyken daha az durakta duran (Express/Hızlı) hat tercih edilir.
6. **`TripId` (Kimlik Bazlı Determinizm) [ASC]:** Tüm metrikler aynıysa, arama her yapıldığında sıralamanın değişmesini (Flaky order) engellemek için TripId alfabetik olarak sıralanır. (Kimlik bazlı tie-breaker).

## 3. Limitasyonlar ve Sınırlamalar
- Algoritma maksimum **1 Aktarmayı (1-Transfer)** destekler. Topolojisi itibarıyla 2 ve üzeri aktarmalar, PostGIS veya GraphDB (Neo4j, pgRouting) kullanılmadığı için RDBMS (PostgreSQL) üzerinde O(N^3) maliyet yarattığından performans gerekçesiyle engellenmiştir.
- İstasyon içi veya duraklar arası transfer yürüyüşleri desteklenmemektedir (İndiği noktadan başka bir noktaya yürüyerek aktarma yapma). Sadece aynı durak ID'sinden geçen araçlara binilebilir.
- Gerçek zamanlı (GTFS-Realtime) GPS gecikme/trafik verisi olmadığı için tüm süreler planlanan statik saatlere göredir.
