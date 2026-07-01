using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Observability;

namespace HPD.Base.Runtime.Observability;

internal static class HPDBaseRuntimeObservability
{
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Runtime);
    public static readonly Meter Meter = new(HPDBaseMeterNames.Runtime);
}
