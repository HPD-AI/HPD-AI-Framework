using System.Collections.Concurrent;

namespace HPD.RAG.Core.Providers.Embedding;

/// <summary>
/// Global static registry for embedding providers. Mirrors VectorStoreDiscovery / ProviderDiscovery pattern.
/// </summary>
public static class EmbeddingDiscovery
{
    private static readonly ConcurrentDictionary<string, Func<IEmbeddingProviderFeatures>> _factories = new();
    private static readonly ConcurrentDictionary<string, Func<string, object?>> _deserializers = new();

    public static void RegisterEmbeddingProviderFactory(Func<IEmbeddingProviderFeatures> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var features = factory();
        _factories[features.ProviderKey] = factory;
    }

    public static void RegisterEmbeddingConfigType<TConfig>(
        string providerKey,
        Func<string, TConfig?> deserializer,
        Func<TConfig, string> serializer) where TConfig : class
    {
        _deserializers[providerKey] = json => deserializer(json);
    }

    public static IEmbeddingProviderFeatures? GetProvider(string providerKey)
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
