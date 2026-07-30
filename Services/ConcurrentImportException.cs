namespace ulasim_veri_servisi.Services;

public class ConcurrentImportException : Exception
{
    public ConcurrentImportException() : base("Sistemde zaten aktif olarak çalışan bir GTFS import işlemi mevcut.")
    {
    }

    public ConcurrentImportException(string message) : base(message)
    {
    }

    public ConcurrentImportException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

