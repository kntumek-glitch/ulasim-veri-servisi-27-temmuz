using System.Threading;
using Microsoft.Extensions.Primitives;

namespace ulasim_veri_servisi.Services;

public class JourneyPlanCacheTokenSource
{
    private CancellationTokenSource _cts = new CancellationTokenSource();

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_cts.Token);

    public void Reset()
    {
        var oldCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        oldCts.Cancel();
        oldCts.Dispose();
    }
}
