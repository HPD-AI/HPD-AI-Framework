using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

/// <summary>
/// Owns HPD.BASE Files activity and metric instruments.
/// </summary>
public static class HPDBaseFilesObservability
{
    /// <summary>Activity source for HPD.BASE Files runtime operations.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Files);

    /// <summary>Meter for HPD.BASE Files runtime metrics.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.Files);
}
