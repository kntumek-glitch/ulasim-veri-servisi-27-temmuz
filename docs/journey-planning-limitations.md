# Journey Planning Limitasyonları ve Kenar Durumlar (Limitations)

Yolculuk planlama API'si (Faz 4) başarıyla tamamlanmış ve 124 kapsamlı senaryo ile (Testcontainers) kanıtlanmış olmasına rağmen, bazı teknik ve mantıksal limitasyonları bünyesinde barındırmaktadır. Geliştiricilerin, Mobil veya Web istemcilerini tasarlarken bu sınırları göz önünde bulundurması önemlidir.

## 1. Yalnızca 1 Aktarma (Maksimum 1-Transfer) Desteği
Şu anki algoritma en fazla `1 Aktarma (1-Transfer)` destekleyecek şekilde yazılmıştır. (Origin -> Yürüme -> Otobüs 1 -> Yürüme Yok/Transfer -> Otobüs 2 -> Yürüme -> Dest).
- **Limitasyon:** Eğer iki nokta arasında sadece 2 veya daha fazla aktarma ile gidilebilen bir yol varsa, API "NO_ROUTE_FOUND" (Hiçbir rota bulunamadı) döner.
- **Neden?:** İlişkisel Veritabanlarında (PostgreSQL), Graph algoritmaları (Dijkstra, A*) veya PostGIS/pgRouting eklentileri olmadan 2 ve üzeri transferin hesabı $O(N^3)$ kombinasyonel patlamaya (Combinatorial Explosion) neden olmaktadır. API yanıt sürelerinin `~100ms` altında kalması için limit uygulanmıştır.
- **Çözüm:** Faz 5 ve sonrasında `pgRouting` veya `Neo4j` veritabanına geçilmesi önerilir.

## 2. Transfer Yürüyüş Limiti (MaxTransferWalkMeters)
Kullanıcının `Otobüs 1`'den indikten sonra `Otobüs 2`'ye binebilmesi için belirli bir yürüme mesafesi içinde olan duraklara (Transfer Table) geçiş yapması desteklenmektedir.
- **Limitasyon:** Konfigürasyon dosyasındaki `MaxTransferWalkMeters` (Örn: 500m) sınırını aşan duraklara yürüyerek aktarma yapılması desteklenmez. Spatial (Uzamsal) arama gridleri bu limite göre optimize edilmiştir.
- **Neden?:** Aktarma için 2 km yürümek mantıklı bir senaryo değildir ve çok büyük çaplı uzamsal aramalar N x N karşılaştırması yaratarak performans darboğazına neden olur.

## 3. Canlı Trafik ve Gerçek Zamanlı Veri (GTFS-Realtime) Eksikliği
API sadece Statik (Planlanmış/GTFS) saatleri hesaba katar.
- **Limitasyon:** Gerçek hayatta otobüs 10 dakika gecikebilir. Algoritmamız 3 dakikalık tamponu (Buffer) yeterli bulup rotayı sunabilir, ancak gecikme sebebiyle kullanıcı gerçek hayatta aracı kaçırabilir.
- **Çözüm:** Mobil uygulama istemcilerine uyarı olarak `DataSourceWarning` metadata mesajı eklenmiştir: *"Sonuçlar statik (planlı) tarife verisine dayanmaktadır, canlı araç konumu/trafiği içermez."*

## 4. Bilet ve Ücret (Fares) Modülü Yokluğu
GTFS verilerindeki `fare_attributes.txt` ve `fare_rules.txt` tabloları parse edilmediği için, hesaplanan bir rotanın "Kullanıcıya kaç TL'ye mal olacağı" (Örn: Aktarma indirimi) desteklenmemektedir.
- **Çözüm:** Biletlendirme hesaplama modülü Faz 6 planlarında yer almaktadır.

## 5. Araç Geçiş Çakışmaları
Farklı ID'ye sahip olan ama fiziksel olarak aynı koordinatta bulunan duraklar algoritma tarafından bağımsız iki durak olarak değerlendirilir. Aynı isimli (`Origin` örneği) ancak ID'si farklı duraklar algoritma tarafından karıştırılmaz, fakat bu da 2. maddedeki transfer yürüyüşünün eksikliğinden dolayı bir dezavantaj yaratır.
