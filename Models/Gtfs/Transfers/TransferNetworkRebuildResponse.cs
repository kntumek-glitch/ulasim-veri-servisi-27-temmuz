namespace TransportDataService.Models.Gtfs.Transfers;

public class TransferNetworkRebuildResponse
{
    public int TransferCount { get; set; }
    public long ExecutionTimeMs { get; set; }
    public int MaxWalkingDistanceMeters { get; set; }
}
