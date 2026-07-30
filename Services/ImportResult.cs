namespace ulasim_veri_servisi.Services
{
    public class ImportResult
    {
        public string SourceName { get; set; } = string.Empty;

        public int ImportedRecordCount { get; set; }

        public int UpdatedRecordCount { get; set; }

        public int FailedRecordCount { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }

        public DateTime FinishedAt { get; set; }
    }
}


