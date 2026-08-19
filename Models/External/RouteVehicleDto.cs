using System.Text.Json.Serialization;

namespace ulasim_veri_servisi.Models.External
{
    public class RouteVehicleDto
    {
        [JsonPropertyName("OtobusId")]
        public int OtobusId { get; set; }

        [JsonPropertyName("Yon")]
        public int Yon { get; set; }

        [JsonPropertyName("KoorX")]
        public string? KoorX { get; set; }

        [JsonPropertyName("KoorY")]
        public string? KoorY { get; set; }
    }
}


