namespace ulasım_veri_servisi.Models.Gtfs
{
    public class RouteDepartureDataDto
    {
        public string DepartureTime { get; set; } = string.Empty;
        public string TripId { get; set; } = string.Empty;
    }

    public class PaginationDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalRecords { get; set; }
    }

    public class RouteDeparturesMetadataDto
    {
        public bool IsFeedExpired { get; set; }
        public bool MissingCalendarDatesFile { get; set; }
    }

    public class RouteDeparturesResponseDto
    {
        public List<RouteDepartureDataDto> Data { get; set; } = new();
        public PaginationDto Pagination { get; set; } = new();
        public RouteDeparturesMetadataDto Metadata { get; set; } = new();
    }
}
