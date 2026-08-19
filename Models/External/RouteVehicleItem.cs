namespace ulasim_veri_servisi.Models.External
{
    public class RouteVehicleItem
    {
        public string BusId { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public string LocationContext { get; set; } = string.Empty;

        public string DestinationName { get; set; } = string.Empty;

        public string OriginDepartureTime { get; set; } = string.Empty;
    }
}
