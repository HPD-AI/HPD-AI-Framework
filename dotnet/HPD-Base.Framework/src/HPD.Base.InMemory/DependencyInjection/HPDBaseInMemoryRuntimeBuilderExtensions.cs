using HPD.Base.InMemory.Configuration;
using HPD.Base.Runtime.Builder;

namespace HPD.Base.InMemory.DependencyInjection;

/// <summary>
/// Adds HPD.BASE InMemory services to an existing HPD.BASE runtime builder.
/// </summary>
public static class HPDBaseInMemoryRuntimeBuilderExtensions
{
    /// <summary>
    /// Registers the HPD.BASE InMemory store services with the runtime builder.
    /// </summary>
    /// <param name="builder">The runtime builder.</param>
    /// <param name="configure">An optional options callback.</param>
    /// <returns>The same runtime builder.</returns>
    public static IHPDBaseRuntimeBuilder AddHPDBaseInMemoryStore(
        this IHPDBaseRuntimeBuilder builder,
        Action<HPDBaseInMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseInMemoryStore(configure);
        return builder;
    }
}
