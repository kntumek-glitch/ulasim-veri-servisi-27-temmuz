namespace ulasim_veri_servisi.Services
{
    public class ApproachingBusItem
    {
        public string BusId { get; set; } = string.Empty;

        public string RouteNumber { get; set; } = string.Empty;

        public string RouteName { get; set; } = string.Empty;

        public int RemainingStopCount { get; set; }

        public string Direction { get; set; } = string.Empty;

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public bool IsAccessible { get; set; }

        public bool HasBicycleRack { get; set; }
    }
}

