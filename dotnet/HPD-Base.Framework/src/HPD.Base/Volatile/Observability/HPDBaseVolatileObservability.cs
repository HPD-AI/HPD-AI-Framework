using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

internal static class HPDBaseVolatileObservability
{
    /// <summary>Provides the activity source value.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Volatile);
    /// <summary>Provides the meter value.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.Volatile);
}
