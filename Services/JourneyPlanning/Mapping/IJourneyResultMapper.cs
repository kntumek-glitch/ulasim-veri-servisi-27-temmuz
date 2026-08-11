using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TransportDataService.Domain;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services.JourneyPlanning.Mapping;

public interface IJourneyResultMapper
{
    ItineraryDto CreateItineraryDto(JourneyPlanSearchRequest request, List<LegDto> legs, string serviceDate);
    Task PopulateIntermediateStopsAsync(List<ItineraryDto> itineraries, TimeZoneInfo tzi, int importId, CancellationToken cancellationToken);
    Task<List<ItineraryDto>> EvaluateOsrmWalksAsync(List<ItineraryDto> candidates, JourneyPlanSearchRequest request, List<GtfsStop> activeStops, int importId, CancellationToken cancellationToken);
}
