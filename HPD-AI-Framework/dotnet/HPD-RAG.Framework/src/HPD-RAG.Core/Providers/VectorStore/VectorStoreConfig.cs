using System.Text.Json;

namespace HPD.RAG.Core.Providers.VectorStore;

/// <summary>
/// Generic config envelope passed to IVectorStoreFeatures.CreateVectorStore.
/// Mirrors ProviderConfig from the HPD Agent provider system.
/// Per-backend typed config classes are stored in ProviderOptions for AOT-safe roundtripping.
/// </summary>
public sealed class VectorStoreConfig
{
    public required string ProviderKey { get; set; }

    /// <summary>
    /// Provider-specific configuration as a JSON/YAML object.
    /// Deserialized via RegisterVectorStoreConfigType's source-generated deserializer lambda.
    /// </summary>
    public JsonElement? ProviderOptions { get; set; }

    /// <summary>
    /// Deserialize ProviderOptions to the backend-specific typed config class.
    /// Returns null if ProviderOptions is null or deserialization returns null.
    /// Uses the AOT-safe deserializer registered by RegisterVectorStoreConfigType.
    /// </summary>
    public T? GetTypedConfig<T>() where T : class
    {
        var providerOptionsJson = GetProviderOptionsRawJson();
        if (string.IsNullOrEmpty(providerOptionsJson))
            return null;

        return VectorStoreDiscovery.DeserializeConfig<T>(ProviderKey, providerOptionsJson);
    }

    public string? GetProviderOptionsRawJson()
        => ProviderOptions?.GetRawText();
}
