namespace ulasim_veri_servisi.Services
{
    public class RouteVehiclesResponse
    {
        public string RouteId { get; set; } = string.Empty;
        public string RouteNumber { get; set; } = string.Empty;

        public DateTime RetrievedAt { get; set; }

        public bool FromCache { get; set; }

        public List<RouteVehicleItem> Vehicles { get; set; } = new();
    }
}

