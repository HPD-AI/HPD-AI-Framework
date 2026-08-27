namespace HPD.Agent.Providers;

/// <summary>
/// Declares how each shipped provider adapter projects streaming usage into M.E.AI updates.
/// An adapter must be added here with a conformance fixture before its usage is accumulated.
/// </summary>
public static class ProviderStreamingUsageSemanticsCatalog
{
    private static readonly IReadOnlyDictionary<(string Provider, ProviderClientFamily Family), UsageUpdateSemantics>
        Declarations = BuildDeclarations();

    public static UsageUpdateSemantics Resolve(
        string? providerKey,
        ProviderClientFamily family,
        ProviderStreamingUsageSemanticsDeclaration? adapterDeclaration = null)
    {
        if (adapterDeclaration is not null)
        {
            if (adapterDeclaration.Family != family)
                throw new InvalidOperationException(
                    $"The adapter usage declaration is for '{adapterDeclaration.Family}', not '{family}'.");
            return adapterDeclaration.Semantics;
        }
        if (string.IsNullOrWhiteSpace(providerKey) ||
            !Declarations.TryGetValue((providerKey, family), out var semantics))
        {
            throw new InvalidOperationException(
                $"Provider adapter '{providerKey ?? "<unknown>"}' has no declared streaming usage semantics for '{family}'.");
        }
        return semantics;
    }

    private static IReadOnlyDictionary<(string, ProviderClientFamily), UsageUpdateSemantics> BuildDeclarations()
    {
        var declarations = new Dictionary<(string, ProviderClientFamily), UsageUpdateSemantics>();
        string[] terminalChatAdapters =
        [
            "anthropic", "azure-ai", "azure-openai", "bedrock", "cerebras", "cohere",
            "dashscope", "deepinfra", "deepseek", "fireworks", "google-ai", "groq",
            "huggingface", "hyperbolic", "lmstudio", "minimax", "mistral", "moonshot",
            "nebius", "nscale", "nvidia-nim", "ollama", "onnx-runtime", "openai",
            "openrouter", "ovhcloud", "perplexity", "replicate", "sambanova", "scaleway",
            "siliconflow", "together", "venice", "xai", "zai", "test"
        ];
        foreach (var provider in terminalChatAdapters)
            declarations.Add((provider, ProviderClientFamily.Chat), UsageUpdateSemantics.FinalOnly);

        declarations.Add(("openai", ProviderClientFamily.Realtime), UsageUpdateSemantics.CumulativeSnapshot);
        declarations.Add(("test", ProviderClientFamily.Realtime), UsageUpdateSemantics.CumulativeSnapshot);
        return declarations;
    }
}

/// <summary>
/// Adapter-owned declaration returned from an M.E.AI client's <c>GetService</c> surface.
/// Custom provider clients use this contract instead of modifying HPD's shipped-adapter catalog.
/// </summary>
public sealed record ProviderStreamingUsageSemanticsDeclaration(
    ProviderClientFamily Family,
    UsageUpdateSemantics Semantics,
    string AdapterId,
    string FixtureId);
