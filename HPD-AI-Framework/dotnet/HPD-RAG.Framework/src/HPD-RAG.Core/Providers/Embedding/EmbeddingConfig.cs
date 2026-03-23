namespace HPD.RAG.Core.Providers.Embedding;

/// <summary>
/// Generic config envelope for embedding provider creation.
/// Per-provider typed config classes are registered via EmbeddingDiscovery.RegisterEmbeddingConfigType.
/// </summary>
public sealed class EmbeddingConfig
{
    public required string ProviderKey { get; set; }
    public required string ModelName { get; set; }
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? ProviderOptionsJson { get; set; }

    public T? GetTypedConfig<T>() where T : class
    {
        if (string.IsNullOrEmpty(ProviderOptionsJson))
            return null;
        return EmbeddingDiscovery.DeserializeConfig<T>(ProviderKey, ProviderOptionsJson);
    }
}
