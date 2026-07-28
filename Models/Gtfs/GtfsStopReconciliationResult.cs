namespace ulasım_veri_servisi.Models.Gtfs
{
    public class GtfsStopReconciliationResult
    {
        public int TotalMatches { get; set; }
        public int StopCodeMatches { get; set; }
        public int MissingInStops { get; set; }
        public int MissingInGtfs { get; set; }
        public int NameMismatches { get; set; }
        public int CoordinateMismatches { get; set; }
        public int ManualReview { get; set; }
    }
}
