using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

/// <summary>
/// Owns HPD.BASE Realtime activity and metric instruments.
/// </summary>
public static class HPDBaseRealtimeObservability
{
    /// <summary>Activity source for HPD.BASE Realtime runtime operations.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Realtime);

    /// <summary>Meter for HPD.BASE Realtime metrics.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.Realtime);
}
