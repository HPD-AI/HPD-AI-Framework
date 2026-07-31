using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

internal static class HPDBaseVolatileObservability
{
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Volatile);
    public static readonly Meter Meter = new(HPDBaseMeterNames.Volatile);
}
