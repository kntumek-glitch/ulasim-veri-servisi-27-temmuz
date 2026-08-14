using System.Text.Json;
using System.Text.Json.Serialization;
using ulasim_veri_servisi.Models.Gtfs;
using Xunit;

namespace TransportDataService.Tests.UnitTests
{
    public class GeoJsonSerializationTests
    {
        [Fact]
        public void GeoJsonFeature_MustSerialize_Properties()
        {
            // Arrange
            var feature = new GeoJsonFeature
            {
                Geometry = new GeoJsonGeometry
                {
                    Coordinates = new List<double[]> { new double[] { 27.123, 38.123 } }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(feature);

            // Assert
            Assert.Contains("\"properties\":{}", json);
        }

        [Fact]
        public void GeoJsonFeature_MustUse_LowerCase_Keys()
        {
            // Arrange
            var feature = new GeoJsonFeature
            {
                Geometry = new GeoJsonGeometry
                {
                    Coordinates = new List<double[]> { new double[] { 27.123, 38.123 } }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(feature);

            // Assert
            Assert.Contains("\"type\":\"Feature\"", json);
            Assert.Contains("\"geometry\":", json);
            Assert.Contains("\"coordinates\":", json);
            Assert.DoesNotContain("\"Type\"", json);
            Assert.DoesNotContain("\"Geometry\"", json);
            Assert.DoesNotContain("\"Coordinates\"", json);
        }
    }
}
