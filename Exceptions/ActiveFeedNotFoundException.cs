using System;

namespace ulasim_veri_servisi.Exceptions;

public class ActiveFeedNotFoundException : Exception
{
    public ActiveFeedNotFoundException() : base("Sistemde işlem yapabilecek aktif bir GTFS veri seti bulunamadı. Lütfen bir veri setinin başarılı bir şekilde içe aktarılmasını (import) bekleyin.")
    {
    }

    public ActiveFeedNotFoundException(string message) : base(message)
    {
    }

    public ActiveFeedNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

