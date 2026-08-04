namespace ulasim_veri_servisi.Models;

public class WalkingRoutingCacheConfiguration
{
    public int TtlMinutes { get; set; } = 1440;
    public int MaxCapacity { get; set; } = 10000;
}
