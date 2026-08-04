using System;
using System.Text.Json.Serialization;

namespace ulasim_veri_servisi.Models.Routing;

public class WalkRoutingResponseDto
{
    [JsonPropertyName("distanceMeters")]
    public double DistanceMeters { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("geometry")]
    public object? Geometry { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "OSRM";

    [JsonPropertyName("isApproximate")]
    public bool IsApproximate { get; set; }

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }

    [JsonPropertyName("retrievedAt")]
    public DateTimeOffset RetrievedAt { get; set; }
}
