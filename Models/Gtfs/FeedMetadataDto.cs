namespace ulasim_veri_servisi.Models.Gtfs
{
    public class FeedMetadataDto
    {
        public string ImportId { get; set; } = string.Empty;
        public DateTime ImportDate { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string? FeedStartDate { get; set; }
        public string? FeedEndDate { get; set; }
        public bool IsStale { get; set; }
        public List<string> MissingFiles { get; set; } = new();
        public string DataVersion { get; set; } = string.Empty;
    }
}

