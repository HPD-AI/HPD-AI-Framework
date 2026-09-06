using HPD.Agent;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.Providers;

namespace HPD.Agent.TUI;

/// <summary>Provides model choices partitioned by exact provider target and connection identity.</summary>
public interface IAgentTuiModelCatalog
{
    /// <summary>Lists exact provider target and connection choices available to the caller.</summary>
    ValueTask<IReadOnlyList<AgentTuiProviderChoice>> GetProvidersAsync(
        AgentTuiModelCatalogContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Lists models for one exact provider target and connection choice.</summary>
    ValueTask<IReadOnlyList<AgentTuiModelChoice>> GetModelsAsync(
        AgentTuiModelCatalogContext context,
        AgentTuiProviderChoice provider,
        AgentTuiModelQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Supplies the active runtime scope and shell to model-catalog operations.</summary>
public sealed class AgentTuiModelCatalogContext
{
    /// <summary>Creates a catalog context for the active runtime scope and shell.</summary>
    /// <param name="scope">The active agent runtime scope.</param>
    /// <param name="shell">The active chat shell.</param>
    public AgentTuiModelCatalogContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    /// <summary>Gets the active agent runtime scope.</summary>
    public AgentTuiRuntimeScope Scope { get; }

    /// <summary>Gets the active chat shell.</summary>
    public ChatShellModel Shell { get; }
}

/// <summary>Describes one exact provider target and connection available for model selection.</summary>
/// <param name="SelectionId">The stable selection identity.</param>
/// <param name="TargetId">The product target identity.</param>
/// <param name="ConnectionId">The immutable product connection identity.</param>
/// <param name="Provider">The complete portable provider reference.</param>
/// <param name="DisplayName">The user-facing choice label.</param>
/// <param name="IsRegistered">Whether the provider target is registered.</param>
/// <param name="IsAuthenticated">Whether this connection is currently usable.</param>
/// <param name="IsExpired">Whether the connection requires reauthorization.</param>
/// <param name="SupportsLiveModelSearch">Whether live model discovery is supported.</param>
/// <param name="SupportsFreeModels">Whether free-only discovery is supported.</param>
/// <param name="Chat">Shared target configuration to carry into the selected chat client.</param>
public sealed record AgentTuiProviderChoice(
    string SelectionId,
    string TargetId,
    string ConnectionId,
    ProviderReference Provider,
    string DisplayName,
    bool IsRegistered,
    bool IsAuthenticated,
    bool IsExpired = false,
    bool SupportsLiveModelSearch = false,
    bool SupportsFreeModels = false,
    ChatClientConfig? Chat = null);

/// <summary>Describes one model-catalog query.</summary>
/// <param name="Search">Optional provider-specific search text.</param>
/// <param name="Live">Whether the catalog should perform live discovery.</param>
/// <param name="FreeOnly">Whether results must be restricted to free models.</param>
public sealed record AgentTuiModelQuery(
    string? Search = null,
    bool Live = false,
    bool FreeOnly = false);

/// <summary>Describes one model bound to an exact provider selection.</summary>
/// <param name="SelectionId">The provider choice identity that owns the model.</param>
/// <param name="ModelId">The provider model identity.</param>
/// <param name="DisplayName">The optional user-facing model name.</param>
/// <param name="IsRecommended">Whether product policy recommends this model.</param>
/// <param name="IsFree">Whether model metadata identifies zero usage cost.</param>
/// <param name="Capabilities">Known model capabilities.</param>
/// <param name="ProviderConfig">Model-specific provider construction constraints supplied by the catalog.</param>
public sealed record AgentTuiModelChoice(
    string SelectionId,
    string ModelId,
    string? DisplayName = null,
    bool IsRecommended = false,
    bool IsFree = false,
    AgentTuiModelCapabilities? Capabilities = null,
    IProviderConfig? ProviderConfig = null);

/// <summary>Describes known product-facing capabilities and metadata for a model.</summary>
/// <param name="SupportsTools">Whether tool invocation is supported.</param>
/// <param name="SupportsReasoning">Whether reasoning controls are supported.</param>
/// <param name="SupportsTemperature">Whether temperature configuration is supported.</param>
/// <param name="SupportsAttachments">Whether attachment input is supported.</param>
/// <param name="ContextWindow">The total context window when known.</param>
/// <param name="InputTokenLimit">The input token limit when known.</param>
/// <param name="OutputTokenLimit">The output token limit when known.</param>
/// <param name="InputModalities">Supported input modalities.</param>
/// <param name="OutputModalities">Supported output modalities.</param>
/// <param name="InputCost">Input cost metadata when known.</param>
/// <param name="OutputCost">Output cost metadata when known.</param>
/// <param name="CacheReadCost">Cache-read cost metadata when known.</param>
/// <param name="CacheWriteCost">Cache-write cost metadata when known.</param>
/// <param name="IsOpenWeights">Whether the model is distributed as open weights.</param>
/// <param name="Family">The optional model family.</param>
/// <param name="ReleaseDate">The optional provider release date.</param>
/// <param name="Status">The optional provider lifecycle status.</param>
/// <param name="SupportedReasoningEfforts">Raw provider levels; unknown levels are retained but not offered.</param>
/// <param name="DefaultReasoningEffort">The advertised provider default, without forcing an explicit request.</param>
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
    string? Status = null,
    IReadOnlyList<string>? SupportedReasoningEfforts = null,
    string? DefaultReasoningEffort = null)
{
    /// <summary>Gets an instance with no asserted capabilities.</summary>
    public static AgentTuiModelCapabilities None { get; } = new();

    /// <summary>Gets whether a positive total context window is known.</summary>
    public bool HasKnownContextWindow => ContextWindow is > 0;
}

/// <summary>Configures the model-selection command and its commit hooks.</summary>
public sealed class AgentTuiModelSelectionOptions
{
    /// <summary>Gets or sets whether selectable models must support tools.</summary>
    public bool RequireToolSupport { get; set; }

    /// <summary>Gets or sets the command registration order.</summary>
    public int Order { get; set; } = Commands.HpdAgentTuiCommandDescriptor.DefaultOrder;

    /// <summary>Gets or sets an optional final configuration step before selection is committed.</summary>
    public Func<AgentTuiCommandContext, AgentTuiSelectedModel, ValueTask<AgentTuiSelectedModel?>>? ConfigureSelection { get; set; }

    /// <summary>Gets or sets an optional callback invoked after selection is committed.</summary>
    public Func<AgentTuiCommandContext, AgentTuiSelectedModel, ValueTask>? SelectionCommitted { get; set; }
}

/// <summary>Owns the current and recent exact model selections for one TUI runtime.</summary>
public sealed class AgentTuiModelSelectionState
{
    private const int MaxRecentSelections = 8;
    private readonly List<AgentTuiSelectedModel> _recent = [];

    /// <summary>Gets the current exact model selection.</summary>
    public AgentTuiSelectedModel? Current { get; private set; }

    /// <summary>Gets whether a current selection exists.</summary>
    public bool HasSelection => Current is not null;

    /// <summary>Gets recent selections partitioned by immutable connection and model identity.</summary>
    public IReadOnlyList<AgentTuiSelectedModel> Recent => _recent;

    /// <summary>Commits an exact provider target, connection, model, and chat configuration.</summary>
    /// <param name="selectionId">The stable selectable-row identity.</param>
    /// <param name="targetId">The product target identity.</param>
    /// <param name="connectionId">The immutable product connection identity.</param>
    /// <param name="provider">The complete portable provider reference.</param>
    /// <param name="modelId">The provider model identity.</param>
    /// <param name="displayName">The optional model display name.</param>
    /// <param name="capabilities">Known model capabilities.</param>
    /// <param name="chat">Shared target and model chat configuration.</param>
    public void Set(
        string selectionId,
        string targetId,
        string connectionId,
        ProviderReference provider,
        string modelId,
        string? displayName = null,
        AgentTuiModelCapabilities? capabilities = null,
        ChatClientConfig? chat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        Current = new AgentTuiSelectedModel(
            selectionId,
            targetId,
            connectionId,
            ProviderClientConfigSnapshot.CloneProviderReference(provider),
            modelId,
            displayName,
            capabilities ?? AgentTuiModelCapabilities.None,
            CloneChat(chat));
        Remember(Current);
    }

    /// <summary>Commits an owned snapshot of an existing selection.</summary>
    /// <param name="model">The exact selection to commit.</param>
    public void Set(AgentTuiSelectedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Set(
            model.SelectionId,
            model.TargetId,
            model.ConnectionId,
            model.Provider,
            model.ModelId,
            model.DisplayName,
            model.Capabilities,
            model.Chat);
    }

    /// <summary>Clears the active selection without erasing recent history.</summary>
    public void Clear()
    {
        Current = null;
    }

    private void Remember(AgentTuiSelectedModel model)
    {
        _recent.RemoveAll(candidate =>
            string.Equals(candidate.ConnectionId, model.ConnectionId, StringComparison.Ordinal) &&
            string.Equals(candidate.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, model);
        if (_recent.Count > MaxRecentSelections)
        {
            _recent.RemoveRange(MaxRecentSelections, _recent.Count - MaxRecentSelections);
        }
    }

    /// <summary>Creates a run configuration containing the exact current provider reference and model.</summary>
    /// <returns>A new run configuration, or <see langword="null"/> when no selection exists.</returns>
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
        config.Provider = ProviderClientConfigSnapshot.CloneProviderReference(selection.Provider);
        config.ModelName = selection.ModelId;
        return config;
    }

    private static ChatClientConfig? CloneChat(ChatClientConfig? source)
        => source is null
            ? null
            : (ChatClientConfig)ProviderClientConfigSnapshot.Clone(source);
}

/// <summary>Captures a model selection with its exact target, connection, and provider reference.</summary>
/// <param name="SelectionId">The stable selection identity.</param>
/// <param name="TargetId">The product target identity.</param>
/// <param name="ConnectionId">The immutable connection identity.</param>
/// <param name="Provider">The complete portable provider reference.</param>
/// <param name="ModelId">The provider model identity.</param>
/// <param name="DisplayName">The optional user-facing model name.</param>
/// <param name="Capabilities">Known model capabilities.</param>
/// <param name="Chat">Provider-specific chat configuration.</param>
public sealed record AgentTuiSelectedModel(
    string SelectionId,
    string TargetId,
    string ConnectionId,
    ProviderReference Provider,
    string ModelId,
    string? DisplayName = null,
    AgentTuiModelCapabilities? Capabilities = null,
    ChatClientConfig? Chat = null);
