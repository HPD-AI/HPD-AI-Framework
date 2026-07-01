using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Observability;

namespace HPD.Base.Sqlite.Observability;

internal static class HPDBaseSqliteObservability
{
    public static readonly ActivitySource ActivitySource = new(HPDBaseActivitySourceNames.Sqlite);
    public static readonly Meter Meter = new(HPDBaseMeterNames.Sqlite);
}
