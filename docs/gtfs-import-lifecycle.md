# GTFS Import Süreçleri ve Yaşam Döngüsü

Bu belge, Ulaşım Veri Servisi'nin (Transportation Data Service) dış kaynaklardan (ESHOT vb.) GTFS verilerini nasıl içeri aktardığını ve eşzamanlılık (concurrency) senaryolarını nasıl yönettiğini açıklamaktadır.

## 1. Eşzamanlılık (Concurrency) ve Lock Mekanizması
Sisteme aynı anda birden fazla Import isteği gelebilir. Race Condition (Yarış Durumu) ve veritabanı tutarsızlıklarını önlemek için **PostgreSQL Advisory Locks** kullanılmaktadır.
- `pg_try_advisory_lock(123456)` çağrısı ile benzersiz bir kilit (lock) alınır.
- Kilit alınamazsa (zaten çalışan bir import varsa), sistem 409 Conflict (ConcurrentImportException) döner.
- İşlem bittiğinde (başarılı veya hatalı) `pg_advisory_unlock(123456)` ile kilit serbest bırakılır.

## 2. Status Geçişleri (Lifecycle)
`GtfsImportRuns` tablosu import işleminin anlık durumunu tutar:
- **Running:** İşlem başladı.
- **Completed:** İşlem başarıyla bitti, yeni veriler yayına alındı.
- **Skipped:** Hedef URL'deki ZIP dosyası (ETag / FileHash) sistemdeki aktif veriyle aynı, yüklemeye gerek yok.
- **Failed:** Ağ hatası, veri bozukluğu veya eksik zorunlu dosya nedeniyle işlem iptal edildi.

Ayrıca, yarıda kesilen ve `Running` durumunda "asılı kalan" (stuck) geçmiş işlemler, yeni bir import başladığında otomatik olarak `Failed` durumuna (Abandoned) çekilir.

## 3. Tablo Temizliği (Clear) ve Performans Optimizasyonu
Milyonlarca satıra sahip (örn. stop_times) büyük tabloların güncellenmesi ciddi bir performans sürecidir.

### Eski (Verimsiz) Yöntem: `RemoveRange`
Başlangıçta Entity Framework Core'un `RemoveRange` metodu kullanılıyordu. Ancak bu metot, silinecek tüm kayıtları önce belleğe (RAM) yüklediği (Select) ve sonrasında teker teker silme (Delete) sorguları ürettiği için devasa boyutlardaki GTFS verisinde "Out Of Memory (OOM)" hatalarına ve çok uzun işlem sürelerine neden olmaktaydı.

### Güncel ve Optimize Yöntem: `ExecuteDeleteAsync`
Şu anda sistemde **yüksek performanslı `ExecuteDeleteAsync()`** metodu kullanılmaktadır.
- Import işlemi için bir Transaction (`BeginTransactionAsync`) başlatılır.
- Veriler henüz ZIP'ten okunup parse edilmeden hemen önce, tüm GTFS tabloları Foreign Key (FK) bağımlılık sırasına uygun olarak (StopTimes -> Trips -> Routes...) `ExecuteDeleteAsync` ile topluca silinir.
- **TRUNCATE alternatifi:** Veritabanına doğrudan `TRUNCATE TABLE` göndermek yerine `ExecuteDeleteAsync` tercih edilmiştir. Bunun sebebi `ExecuteDeleteAsync`'in EF Core 7+ ile tamamen entegre çalışması, verileri RAM'e almadan doğrudan SQL `DELETE` komutuyla silmesi ve veritabanı "Transaction" bloğuna sadık kalmasıdır. Olası bir hatada `Rollback` yapılarak tüm veriler anında geri gelir.
- Opsiyonel dosyalar (`calendar.txt`, `shapes.txt`) ZIP içerisinde bulunmasa dahi bu tablolar transaction'ın en başında temizlendiği için veritabanında "eski veri kalıntısı" (orphaned data) oluşmasının önüne geçilmiştir.

## 4. Veri Ekleme (Insert) Stratejisi
- Temizlenen tablolara veriler Entity Framework'ün `AddRange` metoduyla eklenmektedir.
- Özelikle `stop_times` gibi devasa dosyalar parse edilirken, bellek (RAM) şişmelerini önlemek amacıyla 500'erli yığınlar (Batch / Chunk) halinde `SaveChangesAsync()` çağrılarak işlem güvenle tamamlanır.
