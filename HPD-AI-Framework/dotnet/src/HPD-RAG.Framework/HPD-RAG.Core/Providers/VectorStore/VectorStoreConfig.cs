using System.Text.Json;

namespace HPD.RAG.Core.Providers.VectorStore;

/// <summary>
/// Generic config envelope passed to IVectorStoreFeatures.CreateVectorStore.
/// Mirrors ProviderConfig from the HPD Agent provider system.
/// Per-backend typed config classes are serialized into ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class VectorStoreConfig
{
    public required string ProviderKey { get; set; }
    public string? ConnectionString { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>
    /// Typed per-backend config serialized as JSON. Deserialized via RegisterVectorStoreConfigType's
    /// source-generated deserializer lambda — never via reflection-based JsonSerializer.Deserialize&lt;T&gt;.
    /// </summary>
    public string? ProviderOptionsJson { get; set; }

    /// <summary>
    /// Deserialize the ProviderOptionsJson to the backend-specific typed config class.
    /// Returns null if ProviderOptionsJson is null or deserialization returns null.
    /// Uses the AOT-safe deserializer registered by RegisterVectorStoreConfigType.
    /// </summary>
    public T? GetTypedConfig<T>() where T : class
    {
        if (string.IsNullOrEmpty(ProviderOptionsJson))
            return null;

        return VectorStoreDiscovery.DeserializeConfig<T>(ProviderKey, ProviderOptionsJson);
    }
}
