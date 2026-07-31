using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;

namespace HPD.Base.InMemory;

/// <summary>
/// Owns HPD.BASE Files InMemory provider activity and metric instruments.
/// </summary>
public static class HPDBaseFilesInMemoryObservability
{
    /// <summary>Activity source for HPD.BASE Files InMemory provider operations.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.FilesInMemory);

    /// <summary>Meter for HPD.BASE Files InMemory provider metrics.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.FilesInMemory);
}
