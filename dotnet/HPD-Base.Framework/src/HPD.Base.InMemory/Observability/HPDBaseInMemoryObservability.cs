using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Observability;

namespace HPD.Base.InMemory.Observability;

internal static class HPDBaseInMemoryObservability
{
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.InMemory);
    public static readonly Meter Meter = new(HPDBaseMeterNames.InMemory);
}
