namespace ulasim_veri_servisi.Models.Gtfs
{
    public class GtfsStopReconciliationResult
    {
        public int ExactMatches { get; set; }
        public int StopIdMatchesOnly { get; set; }
        public int StopCodeMatchesOnly { get; set; }
        public int OnlyInGtfs { get; set; }
        public int OnlyInStops { get; set; }
        public int NameMismatches { get; set; }
        public int CoordinateMismatches { get; set; }
        public int ManualReview { get; set; }
    }
}

