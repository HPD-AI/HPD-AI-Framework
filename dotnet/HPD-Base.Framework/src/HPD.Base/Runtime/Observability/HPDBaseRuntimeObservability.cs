using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

internal static class HPDBaseRuntimeObservability
{
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Runtime);
    public static readonly Meter Meter = new(HPDBaseMeterNames.Runtime);
}
