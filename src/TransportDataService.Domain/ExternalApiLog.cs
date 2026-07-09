namespace TransportDataService.Domain;

public class ExternalApiLog
{
    public int Id { get; set; }

    public string EndpointName { get; set; } = string.Empty;

    public string RequestUrl { get; set; } = string.Empty;

    public int HttpStatusCode { get; set; }

    public int ResponseDurationMs { get; set; }

    public bool IsSuccessful { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
}