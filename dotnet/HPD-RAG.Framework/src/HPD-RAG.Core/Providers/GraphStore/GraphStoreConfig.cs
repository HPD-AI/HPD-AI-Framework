using System.Text.Json;

namespace HPD.RAG.Core.Providers.GraphStore;

public sealed class GraphStoreConfig
{
    public required string ProviderKey { get; set; }
    public JsonElement? ProviderOptions { get; set; }

    public T? GetTypedConfig<T>() where T : class
    {
        var providerOptionsJson = GetProviderOptionsRawJson();
        if (string.IsNullOrEmpty(providerOptionsJson))
            return null;
        return GraphStoreDiscovery.DeserializeConfig<T>(ProviderKey, providerOptionsJson);
    }

    public string? GetProviderOptionsRawJson()
        => ProviderOptions?.GetRawText();
}
