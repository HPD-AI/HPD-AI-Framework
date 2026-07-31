using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;

namespace HPD.Base.InMemory;

internal static class HPDBaseInMemoryObservability
{
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.InMemory);
    public static readonly Meter Meter = new(HPDBaseMeterNames.InMemory);
}
