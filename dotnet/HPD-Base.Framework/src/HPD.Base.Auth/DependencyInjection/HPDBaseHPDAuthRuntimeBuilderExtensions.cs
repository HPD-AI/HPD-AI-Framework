using HPD.Base.Auth;
using HPD.Base;

namespace HPD.Base.Auth;

/// <summary>
/// Adds HPD.Auth adapter registration helpers to the BASE runtime builder.
/// </summary>
public static class HPDBaseHPDAuthRuntimeBuilderExtensions
{
    /// <summary>
    /// Adds HPD.Auth adapter services to the runtime builder service collection.
    /// </summary>
    /// <param name="builder">The BASE runtime builder.</param>
    /// <param name="configure">An optional adapter configuration callback.</param>
    /// <returns>The same runtime builder for chaining.</returns>
    public static IHPDBaseRuntimeBuilder AddHPDBaseHPDAuth(
        this IHPDBaseRuntimeBuilder builder,
        Action<HPDBaseHPDAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseHPDAuth(configure);
        return builder;
    }
}
