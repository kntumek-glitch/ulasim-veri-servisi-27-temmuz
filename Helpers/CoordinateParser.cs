using System;
using System.Globalization;

namespace ulasım_veri_servisi.Helpers;

public static class CoordinateParser
{
    public static double Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Coordinate cannot be null or empty.");

        // Nokta veya virgül ayırt etmeksizin parse edebilmek için virgülü noktaya çevirip InvariantCulture kullanıyoruz
        var normalized = value.Replace(',', '.');

        if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        throw new FormatException($"Invalid coordinate format: {value}");
    }
    public static double? ParseNullable(string? value, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace(',', '.');

        if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            if (result >= min && result <= max)
            {
                return result;
            }
        }

        return null;
    }
}
