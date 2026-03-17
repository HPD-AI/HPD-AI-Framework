using HPD.RAG.Core.Providers.GraphStore;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder for configuring a graph store provider.
/// Registers <c>IGraphStore</c> keyed <c>"mrag:graph"</c>.
/// </summary>
public sealed class GraphStoreBuilder
{
    private readonly IServiceCollection _services;
    private Action<IServiceCollection>? _registration;

    internal GraphStoreBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Selects a graph store backend by its provider key (e.g. <c>"neo4j"</c>, <c>"memgraph"</c>).
    /// The backend must be registered via <see cref="GraphStoreDiscovery"/> (automatic via the
    /// provider package's <c>[ModuleInitializer]</c>).
    /// </summary>
    /// <param name="providerKey">Provider key registered by the corresponding <c>HPD.RAG.GraphStoreProviders.*</c> package.</param>
    /// <param name="configure">Optional callback to customize <see cref="GraphStoreConfig"/>.</param>
    public GraphStoreBuilder UseProvider(string providerKey, Action<GraphStoreConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(providerKey);

        _registration = svc =>
        {
            svc.AddKeyedSingleton<IGraphStore>("mrag:graph", (sp, _) =>
            {
                var features = GraphStoreDiscovery.GetProvider(providerKey)
                    ?? throw new InvalidOperationException(
                        $"No graph store provider registered for key '{providerKey}'. " +
                        $"Ensure the corresponding HPD.RAG.GraphStoreProviders.* package is referenced.");

                var config = new GraphStoreConfig { ProviderKey = providerKey };
                configure?.Invoke(config);

                return features.CreateGraphStore(config, sp);
            });
        };

        return this;
    }

    /// <summary>Called by the parent builder after the configure action returns.</summary>
    internal void Register()
    {
        if (_registration == null)
            throw new InvalidOperationException(
                $"No graph store provider was configured. Call UseProvider(...) on the {nameof(GraphStoreBuilder)}.");

        _registration(_services);
    }
}
