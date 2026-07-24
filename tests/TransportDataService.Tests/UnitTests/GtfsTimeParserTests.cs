using FluentAssertions;
using TransportDataService.Domain;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

public class GtfsTimeParserTests
{
    [Theory]
    [InlineData("08:30:00", 30600)] // 8*3600 + 30*60 = 28800 + 1800 = 30600
    [InlineData("24:00:00", 86400)] // 24*3600 = 86400
    [InlineData("25:30:45", 91845)] // 25*3600 + 30*60 + 45 = 90000 + 1800 + 45 = 91845
    [InlineData("00:00:00", 0)]
    public void ParseToSeconds_ValidGtfsTime_ReturnsSeconds(string input, int expectedSeconds)
    {
        // Act
        var result = GtfsTimeParser.ParseToSeconds(input);

        // Assert
        result.Should().Be(expectedSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("25:30")] // Missing seconds
    [InlineData("25:30:45:10")] // Too many parts
    [InlineData("xx:yy:zz")] // Not numbers
    public void ParseToSeconds_InvalidGtfsTime_ReturnsNull(string input)
    {
        // Act
        var result = GtfsTimeParser.ParseToSeconds(input);

        // Assert
        result.Should().BeNull();
    }
}
