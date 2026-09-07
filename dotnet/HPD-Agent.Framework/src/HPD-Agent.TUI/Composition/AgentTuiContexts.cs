using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Controllers;

namespace HPD.Agent.TUI.Composition;

/// <summary>Composes the independent current-agent and direct-child run lanes for one submitted input.</summary>
public delegate AgentTuiInputRunConfig? AgentTuiRunConfigComposer(AgentTuiRunConfigContext context);

/// <summary>Contains the two sibling run-configuration lanes emitted by the TUI.</summary>
/// <param name="RunConfig">Configuration for the agent receiving the input.</param>
/// <param name="SubAgentRunConfig">Configuration for every direct child invoked by the input.</param>
public sealed record AgentTuiInputRunConfig(
    AgentRunConfig? RunConfig = null,
    SubAgentRunConfig? SubAgentRunConfig = null);

public sealed class AgentTuiRunConfigRejectedException : Exception
{
    public AgentTuiRunConfigRejectedException(
        string title,
        string? detail = null,
        TranscriptSeverity severity = TranscriptSeverity.Warning)
        : base(title)
    {
        Title = title;
        Detail = detail;
        Severity = severity;
    }

    public string Title { get; }

    public string? Detail { get; }

    public TranscriptSeverity Severity { get; }
}

public sealed class AgentTuiRunConfigContext
{
    public AgentTuiRunConfigContext(
        AgentTuiExecutionTarget target,
        ChatShellModel shell,
        string prompt)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Scope = target.Scope;
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    }

    public AgentTuiExecutionTarget Target { get; }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public string Prompt { get; }
}

public sealed class AgentTuiShellContext
{
    public AgentTuiShellContext(AgentTuiRuntimeScope scope, ChatShellModel shell)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    public AgentTuiRuntimeScope Scope { get; }

    /// <summary>Gets the complete direct or controlled execution target.</summary>
    public AgentTuiExecutionTarget Target => Shell.Target;

    public ChatShellModel Shell { get; }
}

/// <summary>Provides session state to an application-owned footer item.</summary>
public sealed class AgentTuiFooterContext
{
    /// <summary>Initializes a footer context with isolated component state.</summary>
    public AgentTuiFooterContext(AgentTuiRuntimeScope scope, ChatShellModel shell)
        : this(scope, shell, new AgentTuiStateBag())
    {
    }

    /// <summary>Initializes a footer context with shared session state.</summary>
    public AgentTuiFooterContext(AgentTuiRuntimeScope scope, ChatShellModel shell, AgentTuiStateBag state)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Gets the active runtime scope.</summary>
    public AgentTuiRuntimeScope Scope { get; }

    /// <summary>Gets the mutable shell model.</summary>
    public ChatShellModel Shell { get; }

    /// <summary>Gets shared TUI session state.</summary>
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
