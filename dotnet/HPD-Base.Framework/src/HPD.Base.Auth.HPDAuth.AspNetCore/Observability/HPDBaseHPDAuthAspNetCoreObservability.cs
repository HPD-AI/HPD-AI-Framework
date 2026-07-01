using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Observability;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.Observability;

/// <summary>
/// Owns HPD.BASE HPD.Auth ASP.NET Core bridge activity and metric instruments.
/// </summary>
public static class HPDBaseHPDAuthAspNetCoreObservability
{
    /// <summary>Activity source for HPD.BASE HPD.Auth ASP.NET Core bridge operations.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.HPDAuthAspNetCore);

    /// <summary>Meter for HPD.BASE HPD.Auth ASP.NET Core bridge metrics.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.HPDAuthAspNetCore);
}
