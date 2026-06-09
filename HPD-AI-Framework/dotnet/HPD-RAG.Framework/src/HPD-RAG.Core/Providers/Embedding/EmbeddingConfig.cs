using System.Text.Json;

namespace HPD.RAG.Core.Providers.Embedding;

/// <summary>
/// Generic config envelope for embedding provider creation.
/// Per-provider typed config classes are registered via EmbeddingDiscovery.RegisterEmbeddingConfigType.
/// </summary>
public sealed class EmbeddingConfig
{
    public required string ProviderKey { get; set; }
    public required string ModelName { get; set; }
    public JsonElement? ProviderOptions { get; set; }

    public T? GetTypedConfig<T>() where T : class
    {
        var providerOptionsJson = GetProviderOptionsRawJson();
        if (string.IsNullOrEmpty(providerOptionsJson))
            return null;
        return EmbeddingDiscovery.DeserializeConfig<T>(ProviderKey, providerOptionsJson);
    }

    public string? GetProviderOptionsRawJson()
        => ProviderOptions?.GetRawText();
}
