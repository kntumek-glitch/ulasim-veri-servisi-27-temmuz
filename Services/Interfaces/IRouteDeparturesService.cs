using ulasım_veri_servisi.Models.Gtfs;

namespace ulasım_veri_servisi.Services.Interfaces
{
    public interface IRouteDeparturesService
    {
        Task<RouteDeparturesResponseDto?> GetRouteDeparturesAsync(string routeId, int directionId, DateOnly date, int page, int pageSize);
    }
}
