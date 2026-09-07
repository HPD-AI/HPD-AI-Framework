using HPD.Agent.ModelsDev;
using HPD.Agent.Providers;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.TUI.Console;

internal sealed class ConsoleModelsDevModelCatalog(
    ModelsDevStore store,
    IModelsDevProviderState providerState,
    ModelsDevProviderMappings mappings) : IAgentTuiModelCatalog
{
    public async ValueTask<IReadOnlyList<AgentTuiProviderChoice>> GetProvidersAsync(
        AgentTuiModelCatalogContext context,
        CancellationToken cancellationToken = default)
    {
        var database = (await store.GetSnapshotAsync(cancellationToken: cancellationToken)).Database;
        var providers = new List<AgentTuiProviderChoice>();

        foreach (var (modelsDevProviderId, hpdProviderKey) in mappings.ModelsDevToHpd)
        {
            if (!database.Providers.ContainsKey(modelsDevProviderId))
            {
                continue;
            }

            var status = await providerState.GetStatusAsync(hpdProviderKey, cancellationToken);
            providers.Add(new AgentTuiProviderChoice(
                hpdProviderKey,
                hpdProviderKey,
                hpdProviderKey,
                new ProviderReference { Key = hpdProviderKey },
                DisplayProviderName(hpdProviderKey),
                status.IsRegistered,
                status.IsAuthenticated,
                status.IsExpired,
                SupportsLiveModelSearch: true,
                SupportsFreeModels: true));
        }

        return providers;
    }

    public async ValueTask<IReadOnlyList<AgentTuiModelChoice>> GetModelsAsync(
        AgentTuiModelCatalogContext context,
        AgentTuiProviderChoice providerChoice,
        AgentTuiModelQuery query,
        CancellationToken cancellationToken = default)
    {
        var modelsDevProviderId = mappings.ToModelsDevProviderId(providerChoice.Provider.Key);
        if (modelsDevProviderId is null)
        {
            return [];
        }

        var database = (await store.GetSnapshotAsync(cancellationToken: cancellationToken)).Database;
        if (!database.Providers.TryGetValue(modelsDevProviderId, out var modelsDevProvider))
        {
            return [];
        }

        return modelsDevProvider.Models
            .Where(pair => MatchesQuery(pair.Key, pair.Value, query))
            .OrderByDescending(static pair => IsRecommended(pair.Value))
            .ThenBy(static pair => pair.Value.Cost?.Input == 0m && pair.Value.Cost?.Output == 0m ? 0 : 1)
            .ThenBy(static pair => pair.Value.Name ?? pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new AgentTuiModelChoice(
                providerChoice.SelectionId,
                pair.Key,
                pair.Value.Name,
                IsRecommended(pair.Value),
                IsFree(pair.Value),
                Capabilities: new AgentTuiModelCapabilities(
                    SupportsTools: pair.Value.ToolCall,
                    SupportsReasoning: pair.Value.Reasoning,
                    SupportsTemperature: pair.Value.Temperature,
                    SupportsAttachments: pair.Value.Attachment,
                    ContextWindow: pair.Value.Limit?.Context,
                    InputTokenLimit: pair.Value.Limit?.Input,
                    OutputTokenLimit: pair.Value.Limit?.Output,
                    InputModalities: pair.Value.Modalities?.Input.ToArray(),
                    OutputModalities: pair.Value.Modalities?.Output.ToArray(),
                    InputCost: pair.Value.Cost?.Input,
                    OutputCost: pair.Value.Cost?.Output,
                    CacheReadCost: pair.Value.Cost?.CacheRead,
                    CacheWriteCost: pair.Value.Cost?.CacheWrite,
                    IsOpenWeights: pair.Value.OpenWeights,
                    Family: pair.Value.Family,
                    ReleaseDate: pair.Value.ReleaseDate,
                    Status: pair.Value.Status)))
            .ToArray();
    }

    private static bool MatchesQuery(
        string modelId,
        ModelsDevModel model,
        AgentTuiModelQuery query)
    {
        if (query.FreeOnly && !IsFree(model))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(query.Search)
            || modelId.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
            || (model.Name?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (model.Family?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsFree(ModelsDevModel model)
        => model.Cost?.Input == 0m && model.Cost?.Output == 0m;

    private static bool IsRecommended(ModelsDevModel model)
        => string.Equals(model.Status, "stable", StringComparison.OrdinalIgnoreCase);

    private static string DisplayProviderName(string providerKey)
        => providerKey switch
        {
            "google-ai" => "Google AI",
            "openrouter" => "OpenRouter",
            "huggingface" => "Hugging Face",
            "bedrock" => "Amazon Bedrock",
            _ => string.Join(' ', providerKey.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]))
        };
}
