namespace TransportDataService.Models.Gtfs.Transfers;

public class TransferNetworkStatusResponse
{
    public int ActiveImportId { get; set; }
    public DateTime? CalculationDate { get; set; }
    public int TransferCount { get; set; }
    public int MaxWalkingDistanceMeters { get; set; }
    public string CalculationMethod { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public long? ProcessingTimeMs { get; set; }
    public string DataVersion { get; set; } = string.Empty;
}
