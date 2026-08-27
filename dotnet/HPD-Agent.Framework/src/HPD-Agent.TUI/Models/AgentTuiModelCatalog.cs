using HPD.Agent;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.Providers;

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
    AgentTuiModelCapabilities? Capabilities = null);

public sealed record AgentTuiModelCapabilities(
    bool SupportsTools = false,
    bool SupportsReasoning = false,
    bool SupportsTemperature = false,
    bool SupportsAttachments = false,
    int? ContextWindow = null,
    int? InputTokenLimit = null,
    int? OutputTokenLimit = null,
    IReadOnlyList<string>? InputModalities = null,
    IReadOnlyList<string>? OutputModalities = null,
    decimal? InputCost = null,
    decimal? OutputCost = null,
    decimal? CacheReadCost = null,
    decimal? CacheWriteCost = null,
    bool IsOpenWeights = false,
    string? Family = null,
    string? ReleaseDate = null,
    string? Status = null)
{
    public static AgentTuiModelCapabilities None { get; } = new();

    public bool HasKnownContextWindow => ContextWindow is > 0;
}

public sealed class AgentTuiModelSelectionOptions
{
    public bool RequireToolSupport { get; set; }

    public int Order { get; set; } = Commands.HpdAgentTuiCommandDescriptor.DefaultOrder;

    public Func<AgentTuiCommandContext, AgentTuiSelectedModel, ValueTask<AgentTuiSelectedModel?>>? ConfigureSelection { get; set; }

    public Func<AgentTuiCommandContext, AgentTuiSelectedModel, ValueTask>? SelectionCommitted { get; set; }
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
        AgentTuiModelCapabilities? capabilities = null,
        ChatClientConfig? chat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        Current = new AgentTuiSelectedModel(
            providerKey,
            modelId,
            displayName,
            capabilities ?? AgentTuiModelCapabilities.None,
            CloneChat(chat));
        Remember(Current);
    }

    public void Set(AgentTuiSelectedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Set(
            model.ProviderKey,
            model.ModelId,
            model.DisplayName,
            model.Capabilities,
            model.Chat);
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
                Clients = new AgentClientsConfig
                {
                    Chat = CreateChatSelection(Current)
                }
            };

    private static ChatClientConfig CreateChatSelection(AgentTuiSelectedModel selection)
    {
        var config = CloneChat(selection.Chat) ?? new ChatClientConfig();
        var existing = config.Provider;
        var compatible = existing is not null &&
            string.Equals(existing.Key, selection.ProviderKey, StringComparison.OrdinalIgnoreCase);
        config.Provider = new ProviderReference
        {
            Key = selection.ProviderKey,
            Backend = compatible ? existing!.Backend : null,
            Authentication = compatible && existing!.Authentication is not null
                ? ProviderClientConfigSnapshot.CloneAuthentication(existing.Authentication)
                : null
        };
        config.ModelName = selection.ModelId;
        return config;
    }

    private static ChatClientConfig? CloneChat(ChatClientConfig? source)
        => source is null
            ? null
            : (ChatClientConfig)ProviderClientConfigSnapshot.Clone(source);
}

public sealed record AgentTuiSelectedModel(
    string ProviderKey,
    string ModelId,
    string? DisplayName = null,
    AgentTuiModelCapabilities? Capabilities = null,
    ChatClientConfig? Chat = null);
