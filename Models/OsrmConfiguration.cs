namespace ulasim_veri_servisi.Models;

public class OsrmConfiguration
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Profile { get; set; } = "foot";
    public int TimeoutSeconds { get; set; } = 5;
}
