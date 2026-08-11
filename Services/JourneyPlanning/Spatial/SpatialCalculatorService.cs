using System;
using System.Collections.Generic;
using System.Linq;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services.JourneyPlanning.Models;

namespace ulasim_veri_servisi.Services.JourneyPlanning.Spatial;

public class SpatialCalculatorService : ISpatialCalculatorService
{
    public string GetGridKey(double lat, double lon)
    {
        return $"{Math.Floor(lat / 0.01)}_{Math.Floor(lon / 0.01)}";
    }

    public List<string> GetNeighborGridKeys(double lat, double lon)
    {
        var keys = new List<string>(9);
        int x = (int)Math.Floor(lat / 0.01);
        int y = (int)Math.Floor(lon / 0.01);
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                keys.Add($"{x + i}_{y + j}");
        return keys;
    }

    public double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var R = 6371e3; // metres
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    public List<StopWithDistance> FindStopsWithinRadius(List<GtfsStop> allStops, double lat, double lon, int maxMeters, double walkingSpeed, int maxCandidateStops)
    {
        var result = new List<StopWithDistance>();
        foreach (var stop in allStops)
        {
            var distance = CalculateHaversineDistance(lat, lon, stop.StopLat, stop.StopLon);
            if (distance <= maxMeters)
            {
                result.Add(new StopWithDistance 
                { 
                    Stop = stop, 
                    DistanceMeters = (int)distance,
                    WalkingTimeSeconds = (int)(distance / walkingSpeed)
                });
            }
        }
        return result.OrderBy(x => x.DistanceMeters).Take(maxCandidateStops).ToList();
    }
}
