using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder for configuring a vector store provider.
/// Registers both <c>VectorStore</c> keyed <c>"mrag:vectorstore"</c> and
/// <c>IVectorStoreFeatures</c> keyed <c>"mrag:vectorstore-features"</c>.
/// </summary>
public sealed class VectorStoreBuilder
{
    private readonly IServiceCollection _services;
    private Func<IServiceProvider, (VectorStore Store, IVectorStoreFeatures Features)>? _factory;

    internal VectorStoreBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Selects a vector store backend by its provider key (e.g. <c>"postgres"</c>, <c>"inmemory"</c>,
    /// <c>"qdrant"</c>).
    /// </summary>
    /// <param name="providerKey">
    /// Provider key registered by the corresponding <c>HPD.RAG.VectorStores.*</c> package via
    /// <c>[ModuleInitializer]</c>.
    /// </param>
    /// <param name="configure">Optional callback to customize <see cref="VectorStoreConfig"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown at resolve time if the provider key is not registered in <see cref="VectorStoreDiscovery"/>.
    /// </exception>
    public VectorStoreBuilder UseProvider(string providerKey, Action<VectorStoreConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(providerKey);

        _factory = sp =>
        {
            var features = VectorStoreDiscovery.GetProvider(providerKey)
                ?? throw new InvalidOperationException(
                    $"No vector store provider registered for key '{providerKey}'. " +
                    $"Ensure the corresponding HPD.RAG.VectorStores.* package is referenced.");

            var config = new VectorStoreConfig { ProviderKey = providerKey };
            configure?.Invoke(config);

            var store = features.CreateVectorStore(config, sp);
            return (store, features);
        };

        return this;
    }

    /// <summary>Called by the parent builder after the configure action returns.</summary>
    internal void Register()
    {
        if (_factory == null)
            throw new InvalidOperationException(
                $"No vector store provider was configured. Call UseProvider(...) on the {nameof(VectorStoreBuilder)}.");

        var factory = _factory;

        _services.AddKeyedSingleton<VectorStore>("mrag:vectorstore",
            (sp, _) => factory(sp).Store);

        _services.AddKeyedSingleton<IVectorStoreFeatures>("mrag:vectorstore-features",
            (sp, _) => factory(sp).Features);
    }
}
