using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Controllers;

namespace HPD.Agent.TUI.Composition;

public delegate AgentRunConfig? AgentTuiRunConfigComposer(AgentTuiRunConfigContext context);

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
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        string prompt)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    }

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
