namespace ulasım_veri_servisi.Services
{
    public class RouteVehicleItem
    {
        public string BusId { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }
}
