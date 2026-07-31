using HPD.Base.AspNetCore;
using HPD.Base;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Extension methods for adding HPD.BASE ASP.NET Core services from a Runtime builder.
/// </summary>
public static class HPDBaseAspNetCoreRuntimeBuilderExtensions
{
    /// <summary>
    /// Adds HPD.BASE ASP.NET Core projection services to the builder service collection.
    /// </summary>
    public static IHPDBaseRuntimeBuilder AddHPDBaseAspNetCore(
        this IHPDBaseRuntimeBuilder builder,
        Action<HPDBaseAspNetCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHPDBaseAspNetCore(configure);
        return builder;
    }
}
