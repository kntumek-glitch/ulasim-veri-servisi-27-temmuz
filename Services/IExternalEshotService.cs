namespace ulasim_veri_servisi.Services
{
    public interface IExternalEshotService
    {
        Task<CachedResult<List<EshotBusDto>>> GetApproachingBusesAsync(string externalStopId, CancellationToken cancellationToken = default);

        Task<CachedResult<List<RouteVehicleDto>>> GetRouteVehiclesAsync(string routeNumber, CancellationToken cancellationToken = default);
    }
}

