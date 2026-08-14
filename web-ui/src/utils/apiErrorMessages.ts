export interface ApiError {
  status?: number; // HTTP status code
  code?: string; // backend error code, e.g., 'NO_ROUTE_FOUND'
  message?: string; // optional raw message (not shown to user)
}

/**
 * Returns a user‑friendly Turkish message for a given error.
 * If the error is undefined or does not match known patterns, a generic message is returned.
 */
export function getErrorMessage(error: ApiError | undefined): string {
  if (!error) return 'Bir hata oluştu, lütfen sayfayı yenileyin.';

  // Prefer explicit backend error code when present
  const code = error.code?.toUpperCase();
  switch (code) {
    case 'SUCCESS':
      return 'Başarılı';
    case 'NO_ROUTE_FOUND':
      return 'Bu iki nokta arasında uygun toplu taşıma rotası bulunamadı.';
    case 'NO_ACTIVE_SERVICE':
    case 'FEED_STALE':
      return 'Tarife verisi seçtiğiniz tarih için geçerli değil.';
    case 'FEED_NOT_AVAILABLE':
      return 'GTFS verisi şu anda erişilemez.';
    case 'NO_NEARBY_ORIGIN_STOP':
      return 'Yakın bir başlangıç durağı bulunamadı.';
    case 'NO_NEARBY_DESTINATION_STOP':
      return 'Yakın bir varış durağı bulunamadı.';
    case 'SEARCH_TIMEOUT':
      return 'Arama zamanı aşıldı, lütfen tekrar deneyin.';
    default:
      break;
  }

  // Fallback to HTTP status codes
  switch (error.status) {
    case 429:
      return 'Çok fazla istek gönderildi, bir süre sonra tekrar deneyin.';
    case 500:
      return 'Sunucu hatası oluştu, daha sonra tekrar deneyin.';
    default:
      return 'Bir hata oluştu, lütfen sayfayı yenileyin.';
  }
}
