using System.Text.Json.Serialization;

namespace ulasım_veri_servisi.Services
{
    public class RouteVehiclesApiResponse
    {
        [JsonPropertyName("HataMesaj")]
        public string? HataMesaj { get; set; }

        [JsonPropertyName("HataVarMi")]
        public bool HataVarMi { get; set; }

        [JsonPropertyName("HatOtobusKonumlari")]
        public List<RouteVehicleDto> HatOtobusKonumlari { get; set; } = new();
    }
}