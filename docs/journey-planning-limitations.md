# Journey Planning Limitasyonları ve Kenar Durumlar (Limitations)

Yolculuk planlama API'si (Faz 5) başarıyla tamamlanmış ve 163 kapsamlı senaryo ile (Testcontainers) kanıtlanmış olmasına rağmen, bazı teknik ve mantıksal limitasyonları bünyesinde barındırmaktadır. Geliştiricilerin, Mobil veya Web istemcilerini tasarlarken bu sınırları göz önünde bulundurması önemlidir.

## 1. Maksimum 2 Aktarma (2-Transfer) Desteği
Şu anki algoritma en fazla `2 Aktarma (2-Transfer)` destekleyecek şekilde yazılmıştır. (Origin -> Yürüme -> Araç 1 -> Transfer -> Araç 2 -> Transfer -> Araç 3 -> Yürüme -> Dest).
- **Limitasyon:** Eğer iki nokta arasında sadece 3 veya daha fazla aktarma ile gidilebilen bir yol varsa, API "NO_ROUTE_FOUND" (Hiçbir rota bulunamadı) döner.
- **Neden?:** İlişkisel Veritabanlarında (PostgreSQL), Graph algoritmaları (Dijkstra, A*) veya PostGIS/pgRouting eklentileri olmadan 3 ve üzeri transferin hesabı $O(N^4)$ kombinasyonel patlamaya (Combinatorial Explosion) neden olmaktadır. API yanıt sürelerinin `~100-300ms` altında kalması için kasten 2 aktarma ile limitlenmiştir.
- **Çözüm:** Çoklu aktarmalar (3+) gerekiyorsa, gelecekteki fazlarda `pgRouting` veya `Neo4j` veritabanına geçilmesi gerekir.

## 2. Transfer Yürüyüş Limiti (MaxTransferWalkMeters)
Kullanıcının bir araçtan indikten sonra diğerine binebilmesi için belirli bir yürüme mesafesi içinde olan duraklara (Kalıcı Transfer Ağı) geçiş yapması desteklenmektedir.
- **Limitasyon:** Konfigürasyon dosyasındaki `MaxTransferWalkMeters` (Örn: 500m) sınırını aşan duraklara yürüyerek aktarma yapılması desteklenmez. Spatial (Uzamsal) arama gridleri bu limite göre optimize edilmiştir ve kalıcı ağ bu limit çerçevesinde inşa edilir.
- **Neden?:** Aktarma için 2 km yürümek mantıklı bir senaryo değildir ve çok büyük çaplı uzamsal aramalar N x N karşılaştırması yaratarak performans darboğazına neden olur.

## 3. Canlı Trafik ve Gerçek Zamanlı Veri (GTFS-Realtime) Eksikliği
API sadece Statik (Planlanmış/GTFS) saatleri hesaba katar.
- **Limitasyon:** Gerçek hayatta otobüs 10 dakika gecikebilir. Algoritmamız 3 dakikalık tamponu (Buffer) yeterli bulup rotayı sunabilir, ancak gecikme sebebiyle kullanıcı gerçek hayatta aracı kaçırabilir.
- **Çözüm:** Mobil uygulama istemcilerine uyarı olarak `DataSourceWarning` metadata mesajı eklenmiştir: *"Sonuçlar statik (planlı) tarife verisine dayanmaktadır, canlı araç konumu/trafiği içermez."*

## 4. Bilet ve Ücret (Fares) Modülü Yokluğu
GTFS verilerindeki `fare_attributes.txt` ve `fare_rules.txt` tabloları parse edilmediği için, hesaplanan bir rotanın "Kullanıcıya kaç TL'ye mal olacağı" (Örn: Aktarma indirimi) desteklenmemektedir.
- **Çözüm:** Biletlendirme hesaplama modülü ileriki fazların planlarında yer almaktadır.

## 5. Araç Geçiş Çakışmaları
Farklı ID'ye sahip olan ama fiziksel olarak aynı koordinatta bulunan duraklar algoritma tarafından bağımsız iki durak olarak değerlendirilir. Algoritma Kalıcı Transfer Ağını oluştururken bu durakları "yürüme mesafesinde (0 metre)" bularak birbirlerine bağlar, böylece yolcu aynı peronda farklı ID'li bir durağa teorik olarak transfer yapabilir. Ancak aynı hatta (aynı pattern) tekrar binilmesini engelleyen `Deduplication` (ayıklama) filtresi devrededir.
