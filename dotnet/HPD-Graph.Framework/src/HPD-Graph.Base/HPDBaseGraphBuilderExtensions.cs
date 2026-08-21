using HPD.Base;

namespace HPD.Graph.Base;

/// <summary>Provides HPD-Graph registration helpers for an HPD.Base application graph.</summary>
public static class HPDBaseGraphBuilderExtensions
{
    /// <summary>
    /// Installs one sealed graph activation definition and its graph-owned,
    /// Native-AOT-safe handler factory into the application graph.
    /// </summary>
    /// <param name="builder">The mutable HPD.Base application builder.</param>
    /// <param name="definition">The sealed graph activation definition.</param>
    /// <returns>The same builder for fluent configuration.</returns>
    public static HPDBaseBuilder AddGraphActivation(
        this HPDBaseBuilder builder,
        BaseGraphActivationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definition);
        builder.AddActivation(definition.Registration);
        return builder;
    }
}
