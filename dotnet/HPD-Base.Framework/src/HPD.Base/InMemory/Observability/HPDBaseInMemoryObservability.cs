using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

internal static class HPDBaseInMemoryObservability
{
    /// <summary>Provides the activity source value.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.InMemory);
    /// <summary>Provides the meter value.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.InMemory);
}
