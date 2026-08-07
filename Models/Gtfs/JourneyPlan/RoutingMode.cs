namespace TransportDataService.Models.Gtfs.JourneyPlan;

public enum RoutingMode
{
    /// <summary>
    /// Depart at the specified time and find the earliest arrival.
    /// </summary>
    DEPART_AT,

    /// <summary>
    /// Arrive by the specified time and find the latest departure.
    /// </summary>
    ARRIVE_BY
}
