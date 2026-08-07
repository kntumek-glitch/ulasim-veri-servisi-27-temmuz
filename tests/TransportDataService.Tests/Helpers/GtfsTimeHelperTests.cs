using ulasim_veri_servisi.Helpers;
using Xunit;

namespace TransportDataService.Tests.Helpers
{
    public class GtfsTimeHelperTests
    {
        [Theory]
        [InlineData("25:30:00", 91800)] // (25 * 3600) + (30 * 60) + 0 = 90000 + 1800 = 91800
        [InlineData("24:15:00", 87300)] // (24 * 3600) + (15 * 60) = 86400 + 900 = 87300
        [InlineData("08:00:00", 28800)] // Standart zaman (8 * 3600) = 28800
        [InlineData("00:00:00", 0)]     // Gece yarısı
        [InlineData("27:59:59", 100799)] // Aşırı gece yarısı (Örn: gece 3'teki sefer)
        public void ParseGtfsTimeToSeconds_ShouldReturnCorrectSeconds_WhenFormatIsValid(string timeStr, int expectedSeconds)
        {
            // Act
            var result = GtfsTimeHelper.ParseGtfsTimeToSeconds(timeStr);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedSeconds, result.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("25-30-00")]
        [InlineData("InvalidTime")]
        [InlineData("25:30")] // Eksik saniye alanı
        public void ParseGtfsTimeToSeconds_ShouldReturnNull_WhenFormatIsInvalid(string? invalidTimeStr)
        {
            // Act
            var result = GtfsTimeHelper.ParseGtfsTimeToSeconds(invalidTimeStr!);

            // Assert
            Assert.Null(result);
        }
    }
}

