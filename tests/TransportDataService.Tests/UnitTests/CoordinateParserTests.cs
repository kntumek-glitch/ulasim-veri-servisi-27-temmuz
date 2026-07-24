using FluentAssertions;
using System;
using ulasım_veri_servisi.Helpers;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

public class CoordinateParserTests
{
    [Theory]
    [InlineData("38.423", 38.423)]
    [InlineData("38,423", 38.423)]
    [InlineData("-38.423", -38.423)]
    [InlineData("-38,423", -38.423)]
    [InlineData("0", 0)]
    public void Parse_ValidCoordinates_ReturnsExpectedDouble(string input, double expected)
    {
        // Act
        var result = CoordinateParser.Parse(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_NullOrEmpty_ThrowsArgumentException(string? input)
    {
        // Act
        Action act = () => CoordinateParser.Parse(input);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("Coordinate cannot be null or empty.");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("38.42a")]
    [InlineData("38,42,12")]
    public void Parse_InvalidFormat_ThrowsFormatException(string input)
    {
        // Act
        Action act = () => CoordinateParser.Parse(input);

        // Assert
        act.Should().Throw<FormatException>().WithMessage($"Invalid coordinate format: {input}");
    }

    [Theory]
    [InlineData("38.423", -90, 90, 38.423)]
    [InlineData("-180.0", -180, 180, -180.0)]
    [InlineData("90,0", -90, 90, 90.0)]
    public void ParseNullable_ValidCoordinatesWithinBounds_ReturnsExpectedDouble(string input, double min, double max, double expected)
    {
        var result = CoordinateParser.ParseNullable(input, min, max);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("91.0", -90, 90)]
    [InlineData("-90.1", -90, 90)]
    [InlineData("180.01", -180, 180)]
    [InlineData("-181", -180, 180)]
    public void ParseNullable_CoordinatesOutOfBounds_ReturnsNull(string input, double min, double max)
    {
        var result = CoordinateParser.ParseNullable(input, min, max);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("38.42a")]
    public void ParseNullable_InvalidOrNullOrEmpty_ReturnsNull(string? input)
    {
        var result = CoordinateParser.ParseNullable(input, -90, 90);
        result.Should().BeNull();
    }
}
