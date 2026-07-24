using System.Text.Json.Serialization;

namespace ulasım_veri_servisi.Models.Gtfs
{
    public class GeoJsonShapeResponseDto
    {
        public string? ShapeId { get; set; }
        public string? TripId { get; set; }
        public string? PatternId { get; set; }
        public List<ShapeCoordinateDto> Coordinates { get; set; } = new();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GeoJsonFeature? GeoJson { get; set; }
    }

    public class ShapeCoordinateDto
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public int Sequence { get; set; }
    }

    public class GeoJsonFeature
    {
        public string Type { get; set; } = "Feature";
        public GeoJsonGeometry Geometry { get; set; } = new();
    }

    public class GeoJsonGeometry
    {
        public string Type { get; set; } = "LineString";
        
        // GeoJSON uses [longitude, latitude]
        public List<double[]> Coordinates { get; set; } = new();
    }
}
