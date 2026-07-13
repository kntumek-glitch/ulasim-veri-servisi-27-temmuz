namespace ulasım_veri_servisi.Services
{
    public class ApproachingBusResponse
    {
        public int StopId { get; set; }

        public string ExternalStopId { get; set; } = string.Empty;

        public DateTime RetrievedAt { get; set; }

        public bool FromCache { get; set; }

        public List<ApproachingBusItem> Buses { get; set; } = [];
    }
}