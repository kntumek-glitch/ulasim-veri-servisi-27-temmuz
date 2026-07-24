using TransportDataService.Domain;

namespace TransportDataService.Domain;

public static class GtfsTimeParser
{
    public static int? ParseToSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(':');

        if (parts.Length != 3)
            return null;

        if (!int.TryParse(parts[0], out var hour))
            return null;

        if (!int.TryParse(parts[1], out var minute))
            return null;

        if (!int.TryParse(parts[2], out var second))
            return null;

        return hour * 3600 + minute * 60 + second;
    }
}
