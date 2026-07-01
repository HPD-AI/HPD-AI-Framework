using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Observability;

namespace HPD.Base.Auth.HPDAuth.Observability;

/// <summary>
/// Owns HPD.BASE HPD.Auth adapter activity and metric instruments.
/// </summary>
public static class HPDBaseHPDAuthObservability
{
    /// <summary>Activity source for HPD.BASE HPD.Auth adapter operations.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.HPDAuth);

    /// <summary>Meter for HPD.BASE HPD.Auth adapter metrics.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.HPDAuth);
}
