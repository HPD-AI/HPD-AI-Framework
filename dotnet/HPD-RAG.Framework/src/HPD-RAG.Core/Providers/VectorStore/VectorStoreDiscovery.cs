using System.Collections.Concurrent;

namespace HPD.RAG.Core.Providers.VectorStore;

/// <summary>
/// Global static registry for vector store providers.
/// Mirrors ProviderDiscovery from the HPD Agent provider system.
/// Each HPD.RAG.VectorStores.* package registers itself via [ModuleInitializer]
/// the moment the assembly is loaded — no manual registration required.
/// </summary>
public static class VectorStoreDiscovery
{
    private static readonly ConcurrentDictionary<string, Func<IVectorStoreFeatures>> _factories = new();
    private static readonly ConcurrentDictionary<string, Func<string, object?>> _deserializers = new();
    private static readonly ConcurrentDictionary<string, Func<object, string>> _serializers = new();

    /// <summary>Register a vector store provider factory. Called by [ModuleInitializer] in each provider package.</summary>
    public static void RegisterVectorStoreFactory(Func<IVectorStoreFeatures> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var features = factory();
        _factories[features.ProviderKey] = factory;
    }

    /// <summary>
    /// Register AOT-safe serialize/deserialize lambdas for a backend-specific typed config class.
    /// Called by [ModuleInitializer] in provider packages that have per-backend typed config.
    /// </summary>
    public static void RegisterVectorStoreConfigType<TConfig>(
        string providerKey,
        Func<string, TConfig?> deserializer,
        Func<TConfig, string> serializer) where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        ArgumentNullException.ThrowIfNull(serializer);
        _deserializers[providerKey] = json => deserializer(json);
        _serializers[providerKey] = obj => obj is TConfig typed ? serializer(typed) : throw new InvalidCastException($"Expected {typeof(TConfig).Name}");
    }

    public static IVectorStoreFeatures? GetProvider(string providerKey)
        => _factories.TryGetValue(providerKey, out var factory) ? factory() : null;

    public static IReadOnlyCollection<string> GetRegisteredProviders()
        => _factories.Keys.ToList().AsReadOnly();

    internal static T? DeserializeConfig<T>(string providerKey, string json) where T : class
    {
        if (_deserializers.TryGetValue(providerKey, out var deserializer))
            return deserializer(json) as T;
        return null;
    }
}
