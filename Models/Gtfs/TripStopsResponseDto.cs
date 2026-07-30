namespace ulasim_veri_servisi.Models.Gtfs
{
    public class StopDto
    {
        public string StopId { get; set; } = string.Empty;
        public string StopName { get; set; } = string.Empty;
        public int StopSequence { get; set; }
        public string ArrivalTime { get; set; } = string.Empty;
        public int? ArrivalTimeSeconds { get; set; }
        public string DepartureTime { get; set; } = string.Empty;
        public int? DepartureTimeSeconds { get; set; }
    }

    public class TripStopsResponseDto
    {
        public string TripId { get; set; } = string.Empty;
        public string RouteId { get; set; } = string.Empty;
        public int DirectionId { get; set; }
        public string ServiceId { get; set; } = string.Empty;
        public string? Headsign { get; set; }
        public string? ShapeId { get; set; }
        public List<StopDto> Stops { get; set; } = new List<StopDto>();
    }
}

