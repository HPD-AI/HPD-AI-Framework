namespace HPD.RAG.Core.Providers.GraphStore;

public sealed class GraphStoreConfig
{
    public required string ProviderKey { get; set; }
    public string? Uri { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ConnectionString { get; set; }
    public string? ProviderOptionsJson { get; set; }

    public T? GetTypedConfig<T>() where T : class
    {
        if (string.IsNullOrEmpty(ProviderOptionsJson))
            return null;
        return GraphStoreDiscovery.DeserializeConfig<T>(ProviderKey, ProviderOptionsJson);
    }
}
