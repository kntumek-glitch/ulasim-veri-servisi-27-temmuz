using System;
using System.Globalization;

namespace ulasim_veri_servisi.Helpers;

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
    public static (double? Latitude, double? Longitude) AutoCorrectIzmirCoordinates(double? rawLat, double? rawLon)
    {
        if (rawLat == null || rawLon == null) return (rawLat, rawLon);

        // Izmir bounds roughly: Latitude 37.5 to 39.5, Longitude 26.0 to 28.5
        // If the provided 'rawLat' is actually a longitude (26-29) AND 
        // the provided 'rawLon' is actually a latitude (37-40), we must swap them.
        if (rawLat >= 26.0 && rawLat <= 29.0 && rawLon >= 37.0 && rawLon <= 40.0)
        {
            return (rawLon, rawLat);
        }
        
        // Otherwise assume they are correct (or both are zero, etc.)
        return (rawLat, rawLon);
    }
}

