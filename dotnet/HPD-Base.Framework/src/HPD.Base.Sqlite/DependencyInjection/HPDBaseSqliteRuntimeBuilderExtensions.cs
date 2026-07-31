using HPD.Base;
using HPD.Base.Sqlite;

namespace HPD.Base.Sqlite;

/// <summary>Adds HPD.BASE SQLite services to an existing HPD.BASE runtime builder.</summary>
public static class HPDBaseSqliteRuntimeBuilderExtensions
{
    public static IHPDBaseRuntimeBuilder AddHPDBaseSqliteStore(this IHPDBaseRuntimeBuilder builder, Action<HPDBaseSqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseSqliteStore(configure);
        return builder;
    }
}
