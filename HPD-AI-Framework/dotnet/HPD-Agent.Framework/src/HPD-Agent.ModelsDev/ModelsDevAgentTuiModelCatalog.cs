using HPD.Agent.TUI;

namespace HPD.Agent.ModelsDev;

public sealed class ModelsDevAgentTuiModelCatalog : IAgentTuiModelCatalog
{
    private readonly ModelsDevStore _store;
    private readonly IModelsDevProviderState _providerState;
    private readonly ModelsDevProviderMappings _mappings;

    public ModelsDevAgentTuiModelCatalog(
        ModelsDevStore store,
        IModelsDevProviderState providerState,
        ModelsDevProviderMappings? mappings = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _providerState = providerState ?? throw new ArgumentNullException(nameof(providerState));
        _mappings = mappings ?? ModelsDevProviderMappings.Default;
    }

    public async ValueTask<IReadOnlyList<AgentTuiProviderChoice>> GetProvidersAsync(
        AgentTuiModelCatalogContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var database = await _store.GetDatabaseAsync(cancellationToken);
        var providers = new List<AgentTuiProviderChoice>();

        foreach (var pair in database.Providers.OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var hpdProviderKey = _mappings.ToHpdProviderKey(pair.Key);
            if (string.IsNullOrWhiteSpace(hpdProviderKey))
            {
                continue;
            }

            var status = await _providerState.GetStatusAsync(hpdProviderKey, cancellationToken);
            providers.Add(new AgentTuiProviderChoice(
                ProviderKey: hpdProviderKey,
                DisplayName: ToDisplayName(pair.Key),
                IsRegistered: status.IsRegistered,
                IsAuthenticated: status.IsAuthenticated,
                IsExpired: status.IsExpired,
                SupportsLiveModelSearch: true,
                SupportsFreeModels: true));
        }

        return providers;
    }

    public async ValueTask<IReadOnlyList<AgentTuiModelChoice>> GetModelsAsync(
        AgentTuiModelCatalogContext context,
        string providerKey,
        AgentTuiModelQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(query);

        var modelsDevProviderId = _mappings.ToModelsDevProviderId(providerKey);
        if (string.IsNullOrWhiteSpace(modelsDevProviderId))
        {
            return [];
        }

        var database = await _store.GetDatabaseAsync(cancellationToken);
        if (!database.Providers.TryGetValue(modelsDevProviderId, out var provider))
        {
            return [];
        }

        var choices = new List<AgentTuiModelChoice>();
        foreach (var pair in provider.Models)
        {
            var model = pair.Value;
            if (!IsChatModel(model)
                || IsDeprecated(model)
                || IsEmbeddingModel(model)
                || query.FreeOnly && !IsFree(model)
                || !MatchesSearch(pair.Key, model, query.Search))
            {
                continue;
            }

            choices.Add(new AgentTuiModelChoice(
                ProviderKey: providerKey,
                ModelId: pair.Key,
                DisplayName: model.Name,
                IsRecommended: IsRecommended(pair.Key, model),
                IsFree: IsFree(model),
                SupportsTools: model.ToolCall));
        }

        return choices
            .OrderByDescending(static m => m.IsRecommended)
            .ThenBy(static m => m.DisplayName ?? m.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsChatModel(ModelsDevModel model)
        => model.Modalities?.Output.Any(static modality =>
            string.Equals(modality, "text", StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsDeprecated(ModelsDevModel model)
        => string.Equals(model.Status, "deprecated", StringComparison.OrdinalIgnoreCase);

    private static bool IsEmbeddingModel(ModelsDevModel model)
    {
        var family = model.Family ?? string.Empty;
        var name = model.Name ?? string.Empty;
        return family.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || name.Contains("embed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFree(ModelsDevModel model)
        => model.Cost is not null
            && model.Cost.Input is <= 0
            && model.Cost.Output is <= 0;

    private static bool IsRecommended(string modelId, ModelsDevModel model)
        => model.ToolCall
            && !IsDeprecated(model)
            && !modelId.Contains("preview", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("beta", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSearch(
        string modelId,
        ModelsDevModel model,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return modelId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (model.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
            || (model.Family?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string ToDisplayName(string providerId)
        => providerId switch
        {
            "openai" => "OpenAI",
            "anthropic" => "Anthropic",
            "google" => "Google",
            "openrouter" => "OpenRouter",
            "mistral" => "Mistral",
            "huggingface" => "Hugging Face",
            "amazon-bedrock" => "Amazon Bedrock",
            "ollama" => "Ollama",
            _ => providerId
        };
}
