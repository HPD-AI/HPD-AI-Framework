using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

/// <summary>
/// Owns HPD.BASE Files Volatile provider activity and metric instruments.
/// </summary>
public static class HPDBaseFilesVolatileObservability
{
    /// <summary>Activity source for HPD.BASE Files Volatile provider operations.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.FilesVolatile);

    /// <summary>Meter for HPD.BASE Files Volatile provider metrics.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.FilesVolatile);
}
