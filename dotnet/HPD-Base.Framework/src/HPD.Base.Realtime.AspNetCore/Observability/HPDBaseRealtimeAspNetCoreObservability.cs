using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Observability;

namespace HPD.Base.Realtime.AspNetCore.Observability;

/// <summary>
/// Owns HPD.BASE Realtime ASP.NET Core activity and metric instruments.
/// </summary>
public static class HPDBaseRealtimeAspNetCoreObservability
{
    /// <summary>Activity source for HPD.BASE Realtime ASP.NET Core operations.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.RealtimeAspNetCore);

    /// <summary>Meter for HPD.BASE Realtime ASP.NET Core metrics.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.RealtimeAspNetCore);
}
