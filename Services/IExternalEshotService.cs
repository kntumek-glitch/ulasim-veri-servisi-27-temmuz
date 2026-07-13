namespace ulasım_veri_servisi.Services
{
    public interface IExternalEshotService
    {
        Task<CachedResult<List<EshotBusDto>>> GetApproachingBusesAsync(string externalStopId);

        Task<CachedResult<List<RouteVehicleDto>>> GetRouteVehiclesAsync(string routeNumber);
    }
}
