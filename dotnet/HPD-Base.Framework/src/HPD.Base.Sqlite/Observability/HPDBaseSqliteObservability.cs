using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;

namespace HPD.Base.Sqlite;

internal static class HPDBaseSqliteObservability
{
    /// <summary>Provides the activity source value.</summary>
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Sqlite);
    /// <summary>Provides the meter value.</summary>
    public static readonly Meter Meter = new(HPDBaseMeterNames.Sqlite);
}
