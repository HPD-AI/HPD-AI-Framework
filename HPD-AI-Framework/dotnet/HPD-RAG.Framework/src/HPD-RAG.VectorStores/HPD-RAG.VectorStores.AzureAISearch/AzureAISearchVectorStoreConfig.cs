namespace HPD.RAG.VectorStores.AzureAISearch;

/// <summary>
/// Azure AI Search-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class AzureAISearchVectorStoreConfig
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
}
