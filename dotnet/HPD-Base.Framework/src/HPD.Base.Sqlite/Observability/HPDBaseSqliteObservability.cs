using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;

namespace HPD.Base.Sqlite;

internal static class HPDBaseSqliteObservability
{
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Sqlite);
    public static readonly Meter Meter = new(HPDBaseMeterNames.Sqlite);
}
