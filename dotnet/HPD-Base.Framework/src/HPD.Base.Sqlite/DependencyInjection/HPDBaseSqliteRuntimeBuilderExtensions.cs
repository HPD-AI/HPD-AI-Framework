using HPD.Base.Runtime.Builder;
using HPD.Base.Sqlite.Configuration;

namespace HPD.Base.Sqlite.DependencyInjection;

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
