using HPD.Agent;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI;

public interface IAgentTuiModelCatalog
{
    ValueTask<IReadOnlyList<AgentTuiProviderChoice>> GetProvidersAsync(
        AgentTuiModelCatalogContext context,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AgentTuiModelChoice>> GetModelsAsync(
        AgentTuiModelCatalogContext context,
        string providerKey,
        AgentTuiModelQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class AgentTuiModelCatalogContext
{
    public AgentTuiModelCatalogContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }
}

public sealed record AgentTuiProviderChoice(
    string ProviderKey,
    string DisplayName,
    bool IsRegistered,
    bool IsAuthenticated,
    bool IsExpired = false,
    bool SupportsLiveModelSearch = false,
    bool SupportsFreeModels = false);

public sealed record AgentTuiModelQuery(
    string? Search = null,
    bool Live = false,
    bool FreeOnly = false);

public sealed record AgentTuiModelChoice(
    string ProviderKey,
    string ModelId,
    string? DisplayName = null,
    bool IsRecommended = false,
    bool IsFree = false,
    bool SupportsTools = false);

public sealed class AgentTuiModelSelectionState
{
    public AgentTuiSelectedModel? Current { get; private set; }

    public bool HasSelection => Current is not null;

    public void Set(
        string providerKey,
        string modelId,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        Current = new AgentTuiSelectedModel(providerKey, modelId, displayName);
    }

    public void Set(AgentTuiModelChoice model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Set(model.ProviderKey, model.ModelId, model.DisplayName);
    }

    public void Clear()
    {
        Current = null;
    }

    public AgentRunConfig? ToRunConfig()
        => Current is null
            ? null
            : new AgentRunConfig
            {
                ProviderKey = Current.ProviderKey,
                ModelId = Current.ModelId
            };
}

public sealed record AgentTuiSelectedModel(
    string ProviderKey,
    string ModelId,
    string? DisplayName = null);
