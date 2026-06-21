using System.Collections.Concurrent;

namespace HPD.RAG.Core.Providers.GraphStore;

/// <summary>Global static registry for graph store providers. Mirrors VectorStoreDiscovery pattern.</summary>
public static class GraphStoreDiscovery
{
    private static readonly ConcurrentDictionary<string, Func<IGraphStoreFeatures>> _factories = new();
    private static readonly ConcurrentDictionary<string, Func<string, object?>> _deserializers = new();

    public static void RegisterGraphStoreFactory(Func<IGraphStoreFeatures> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var features = factory();
        _factories[features.ProviderKey] = factory;
    }

    public static void RegisterGraphStoreConfigType<TConfig>(
        string providerKey,
        Func<string, TConfig?> deserializer,
        Func<TConfig, string> serializer) where TConfig : class
    {
        _deserializers[providerKey] = json => deserializer(json);
    }

    public static IGraphStoreFeatures? GetProvider(string providerKey)
        => _factories.TryGetValue(providerKey, out var factory) ? factory() : null;

    public static IReadOnlyCollection<string> GetRegisteredProviders()
        => _factories.Keys.ToList().AsReadOnly();

    internal static T? DeserializeConfig<T>(string providerKey, string json) where T : class
    {
        if (_deserializers.TryGetValue(providerKey, out var d))
            return d(json) as T;
        return null;
    }
}
