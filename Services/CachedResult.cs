namespace ulasım_veri_servisi.Services
{
    public class CachedResult<T>
    {
        public T Data { get; set; } = default!;

        public bool FromCache { get; set; }
    }
}