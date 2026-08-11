using System.Collections.Generic;
using TransportDataService.Domain;
using ulasim_veri_servisi.Services.JourneyPlanning.Models;

namespace ulasim_veri_servisi.Services.JourneyPlanning.Spatial;

public interface ISpatialCalculatorService
{
    string GetGridKey(double lat, double lon);
    List<string> GetNeighborGridKeys(double lat, double lon);
    double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2);
    List<StopWithDistance> FindStopsWithinRadius(List<GtfsStop> allStops, double lat, double lon, int maxMeters, double walkingSpeed, int maxCandidateStops);
}
