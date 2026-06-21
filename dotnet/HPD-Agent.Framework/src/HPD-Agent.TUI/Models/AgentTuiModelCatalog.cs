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

public sealed class AgentTuiModelSelectionOptions
{
    public bool RequireToolSupport { get; set; }
}

public sealed class AgentTuiModelSelectionState
{
    private const int MaxRecentSelections = 8;
    private readonly List<AgentTuiSelectedModel> _recent = [];

    public AgentTuiSelectedModel? Current { get; private set; }

    public bool HasSelection => Current is not null;

    public IReadOnlyList<AgentTuiSelectedModel> Recent => _recent;

    public void Set(
        string providerKey,
        string modelId,
        string? displayName = null,
        bool supportsTools = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        Current = new AgentTuiSelectedModel(providerKey, modelId, displayName, supportsTools);
        Remember(Current);
    }

    public void Set(AgentTuiModelChoice model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Set(model.ProviderKey, model.ModelId, model.DisplayName, model.SupportsTools);
    }

    public void Clear()
    {
        Current = null;
    }

    private void Remember(AgentTuiSelectedModel model)
    {
        _recent.RemoveAll(candidate =>
            string.Equals(candidate.ProviderKey, model.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, model);
        if (_recent.Count > MaxRecentSelections)
        {
            _recent.RemoveRange(MaxRecentSelections, _recent.Count - MaxRecentSelections);
        }
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
    string? DisplayName = null,
    bool SupportsTools = false);
