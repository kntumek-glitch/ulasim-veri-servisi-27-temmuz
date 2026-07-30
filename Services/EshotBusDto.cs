using System.Text.Json.Serialization;
namespace ulasim_veri_servisi.Services
{
    public class EshotBusDto
    {
        
        
            [JsonPropertyName("KalanDurakSayisi")]
            public int KalanDurakSayisi { get; set; }

            [JsonPropertyName("HattinYonu")]
            public int HattinYonu { get; set; }

            [JsonPropertyName("KoorY")]
            public string? KoorY { get; set; }

            [JsonPropertyName("BisikletAparatliMi")]
            public bool BisikletAparatliMi { get; set; }

            [JsonPropertyName("KoorX")]
            public string? KoorX { get; set; }

            [JsonPropertyName("EngelliMi")]
            public bool EngelliMi { get; set; }

            [JsonPropertyName("HatNumarasi")]
            public int HatNumarasi { get; set; }

            [JsonPropertyName("HatAdi")]
            public string? HatAdi { get; set; }

            [JsonPropertyName("OtobusId")]
            public int OtobusId { get; set; }
        

    }
}


