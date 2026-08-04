using System.Text.Json.Serialization;

namespace ulasim_veri_servisi.Models.Routing;

public class WalkRoutingRequestDto
{
    [JsonPropertyName("origin")]
    public RoutingCoordinate Origin { get; set; } = new();

    [JsonPropertyName("destination")]
    public RoutingCoordinate Destination { get; set; } = new();

    [JsonPropertyName("includeGeometry")]
    public bool IncludeGeometry { get; set; }
}

public class RoutingCoordinate
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}
