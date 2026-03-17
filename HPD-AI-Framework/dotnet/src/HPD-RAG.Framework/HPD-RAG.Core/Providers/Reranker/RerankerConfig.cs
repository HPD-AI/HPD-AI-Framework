namespace HPD.RAG.Core.Providers.Reranker;

public sealed class RerankerConfig
{
    public required string ProviderKey { get; set; }
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? ModelName { get; set; }
    public string? ProviderOptionsJson { get; set; }

    public T? GetTypedConfig<T>() where T : class
    {
        if (string.IsNullOrEmpty(ProviderOptionsJson))
            return null;
        return RerankerDiscovery.DeserializeConfig<T>(ProviderKey, ProviderOptionsJson);
    }
}
