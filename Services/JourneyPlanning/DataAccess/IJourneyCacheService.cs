using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ulasim_veri_servisi.Services.JourneyPlanning.Models;

namespace ulasim_veri_servisi.Services.JourneyPlanning.DataAccess;

public interface IJourneyCacheService
{
    Task<ActiveStopsCache> GetActiveStopsAsync(int activeRunId, CancellationToken cancellationToken);
    Task<List<string>> GetActiveServiceIdsAsync(int activeRunId, DateTime date, CancellationToken cancellationToken);
}
