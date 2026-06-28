using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Controllers;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiRunConfigContributor
{
    void ConfigureRun(
        AgentTuiRunConfigContributionContext context,
        AgentRunConfigBuilder builder);
}

public sealed class AgentTuiRunConfigContributionContext
{
    public AgentTuiRunConfigContributionContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        string promptText,
        HpdAgentTuiRegistry registry,
        AgentTuiStateBag state)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        PromptText = promptText ?? throw new ArgumentNullException(nameof(promptText));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public string PromptText { get; }

    public HpdAgentTuiRegistry Registry { get; }

    public AgentTuiStateBag State { get; }
}

public sealed class AgentRunConfigBuilder
{
    private readonly Dictionary<string, IAgentMiddleware> _runtimeMiddleware = new(StringComparer.Ordinal);
    private readonly Dictionary<string, clientToolHarnessDefinition> _clientToolHarnesses = new(StringComparer.Ordinal);

    public AgentRunConfig Config { get; } = new();

    public void SetProviderModel(string? providerKey, string? modelId)
    {
        Config.ProviderKey = providerKey;
        Config.ModelId = modelId;
    }

    public void SetProviderOptions(System.Text.Json.JsonElement providerOptions)
    {
        Config.ProviderOptions = providerOptions;
    }

    public void AddCustomHeader(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Config.CustomHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Config.CustomHeaders[key] = value;
    }

    public void AddContextOverride(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Config.ContextOverrides ??= new Dictionary<string, object>(StringComparer.Ordinal);
        Config.ContextOverrides[key] = value;
    }

    public void AddAdditionalSystemInstructions(string key, string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        Config.AdditionalSystemInstructions = string.IsNullOrWhiteSpace(Config.AdditionalSystemInstructions)
            ? instructions
            : $"{Config.AdditionalSystemInstructions}\n\n{instructions}";
    }

    public void AddPermissionOverride(string functionName, bool requiresPermission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        Config.PermissionOverrides ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        Config.PermissionOverrides[functionName] = requiresPermission;
    }

    public void AddRuntimeMiddleware(string key, IAgentMiddleware middleware)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(middleware);
        _runtimeMiddleware[key] = middleware;
        Config.RuntimeMiddleware = _runtimeMiddleware.Values.ToArray();
    }

    public void AddClientToolHarness(clientToolHarnessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        _clientToolHarnesses[definition.Name] = definition;
        Config.ClientToolInput = (Config.ClientToolInput ?? new AgentClientInput()) with
        {
            clientToolHarnesses = _clientToolHarnesses.Values.ToArray()
        };
    }

    public void AddRuntimeContext(string toolName, IToolMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(metadata);
        Config.ContextInstances ??= new Dictionary<string, IToolMetadata>(StringComparer.Ordinal);
        Config.ContextInstances[toolName] = metadata;
    }

    public void AddAdditionalTool(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var tools = Config.AdditionalTools?.ToList() ?? [];
        tools.Add(tool);
        Config.AdditionalTools = tools;
    }
}

internal sealed class DelegateAgentTuiRunConfigContributor : IAgentTuiRunConfigContributor
{
    private readonly Action<AgentTuiRunConfigContributionContext, AgentRunConfigBuilder> _configure;

    public DelegateAgentTuiRunConfigContributor(
        Action<AgentTuiRunConfigContributionContext, AgentRunConfigBuilder> configure)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    public void ConfigureRun(
        AgentTuiRunConfigContributionContext context,
        AgentRunConfigBuilder builder)
        => _configure(context, builder);
}

public sealed class AgentTuiShellContext
{
    public AgentTuiShellContext(AgentTuiRuntimeScope scope, ChatShellModel shell)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }
}

public sealed class AgentTuiStatusContext
{
    public AgentTuiStatusContext(AgentTuiRuntimeScope scope, ChatShellModel shell)
        : this(scope, shell, new AgentTuiStateBag())
    {
    }

    public AgentTuiStatusContext(AgentTuiRuntimeScope scope, ChatShellModel shell, AgentTuiStateBag state)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiStateBag State { get; }
}

public sealed class AgentTuiWidgetContext
{
    public AgentTuiWidgetContext(TuiSlot slot, AgentTuiRuntimeScope scope, ChatShellModel shell)
        : this(slot, scope, shell, new AgentTuiStateBag())
    {
    }

    public AgentTuiWidgetContext(TuiSlot slot, AgentTuiRuntimeScope scope, ChatShellModel shell, AgentTuiStateBag state)
    {
        Slot = slot;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public TuiSlot Slot { get; }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiStateBag State { get; }
}

public sealed class AgentTuiAutocompleteContext
{
    public AgentTuiAutocompleteContext(
        AutocompleteRequest request,
        AgentTuiRuntimeScope? scope,
        ChatShellModel? shell)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Scope = scope;
        Shell = shell;
    }

    public AutocompleteRequest Request { get; }

    public AutocompleteTrigger? Trigger => Request.Trigger;

    public char? Marker => Trigger?.Marker;

    public int QueryStart => Trigger?.QueryStart ?? Request.Cursor;

    public int QueryLength => Trigger?.QueryLength ?? 0;

    public int Start => Trigger?.Start ?? Request.Cursor;

    public int Length => Trigger?.Length ?? 0;

    public int Cursor => Request.Cursor;

    public bool IsForced => Request.IsForced;

    public bool QueryEquals(string value, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        => Request.TriggerQueryEquals(value, comparison);

    public bool QueryIsPrefixOf(string value, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        => Trigger is { } trigger && Request.SliceIsPrefixOf(trigger.QueryStart, trigger.QueryLength, value, comparison);

    public string GetQueryText() => Request.GetTriggerQuery();

    public string GetText(int start, int length) => Request.GetText(start, length);

    public AgentTuiRuntimeScope? Scope { get; }

    public ChatShellModel? Shell { get; }
}
