namespace HPD.Agent.ModelsDev;

public sealed record ModelsDevProviderMapping(
    string ModelsDevProviderId,
    string HpdProviderKey);

public sealed class ModelsDevProviderMappings
{
    private readonly Dictionary<string, string> _modelsDevToHpd;
    private readonly Dictionary<string, string> _hpdToModelsDev;

    public ModelsDevProviderMappings(IEnumerable<ModelsDevProviderMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        _modelsDevToHpd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _hpdToModelsDev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            Add(mapping);
        }
    }

    public static ModelsDevProviderMappings Default { get; } = new(
    [
        new("openai", "openai"),
        new("anthropic", "anthropic"),
        new("google", "google-ai"),
        new("mistral", "mistral"),
        new("openrouter", "openrouter"),
        new("huggingface", "huggingface"),
        new("amazon-bedrock", "bedrock"),
        new("ollama", "ollama"),
        new("cerebras", "cerebras"),
        new("cohere", "cohere"),
        new("deepinfra", "deepinfra"),
        new("deepseek", "deepseek"),
        new("fireworks", "fireworks"),
        new("groq", "groq"),
        new("minimax", "minimax"),
        new("moonshot", "moonshot"),
        new("nebius", "nebius"),
        new("nvidia", "nvidia-nim"),
        new("perplexity", "perplexity"),
        new("replicate", "replicate"),
        new("sambanova", "sambanova"),
        new("together", "together"),
        new("xai", "xai"),
        new("z-ai", "zai")
    ]);

    public IReadOnlyDictionary<string, string> ModelsDevToHpd => _modelsDevToHpd;

    public IReadOnlyDictionary<string, string> HpdToModelsDev => _hpdToModelsDev;

    public string? ToHpdProviderKey(string modelsDevProviderId)
        => _modelsDevToHpd.TryGetValue(modelsDevProviderId, out var providerKey)
            ? providerKey
            : null;

    public string? ToModelsDevProviderId(string hpdProviderKey)
        => _hpdToModelsDev.TryGetValue(hpdProviderKey, out var providerId)
            ? providerId
            : null;

    private void Add(ModelsDevProviderMapping mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapping.ModelsDevProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapping.HpdProviderKey);
        _modelsDevToHpd[mapping.ModelsDevProviderId] = mapping.HpdProviderKey;
        _hpdToModelsDev[mapping.HpdProviderKey] = mapping.ModelsDevProviderId;
    }
}
