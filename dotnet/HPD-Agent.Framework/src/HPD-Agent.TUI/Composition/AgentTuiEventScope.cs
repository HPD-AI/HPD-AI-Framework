namespace HPD.Agent.TUI.Composition;

/// <summary>Controls which runtime-tree event origins may reach one TUI handler registration.</summary>
[Flags]
public enum AgentTuiEventScope
{
    /// <summary>Events emitted for the thread currently selected by the TUI.</summary>
    CurrentThread = 1,

    /// <summary>Events bubbled from descendant agent threads in the selected runtime tree.</summary>
    Descendants = 2,

    /// <summary>Events from both the selected thread and all of its runtime descendants.</summary>
    CurrentThreadAndDescendants = CurrentThread | Descendants
}

/// <summary>A TUI contribution paired with its runtime-tree event visibility.</summary>
public sealed record AgentTuiEventContribution<T>(
    string Key,
    T Value,
    AgentTuiEventScope Scope);

internal static class AgentTuiEventScopeRouting
{
    public static void Validate(AgentTuiEventScope scope, string parameterName)
    {
        if (scope == 0 || (scope & ~AgentTuiEventScope.CurrentThreadAndDescendants) != 0)
            throw new ArgumentOutOfRangeException(parameterName, scope, "Select at least one valid TUI event scope.");
    }

    public static bool Includes(
        this AgentTuiEventScope scope,
        AgentEvent evt,
        Runtime.AgentTuiRuntimeScope selectedScope)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(selectedScope);

        var origin = string.IsNullOrWhiteSpace(evt.SessionId) || string.IsNullOrWhiteSpace(evt.ThreadId) ||
            string.Equals(evt.SessionId, selectedScope.SessionId, StringComparison.Ordinal) &&
            string.Equals(evt.ThreadId, selectedScope.ThreadId, StringComparison.Ordinal)
                ? AgentTuiEventScope.CurrentThread
                : AgentTuiEventScope.Descendants;
        return (scope & origin) != 0;
    }
}

internal sealed record AgentTuiEventHandlerRegistration(
    IAgentTuiEventHandler Handler,
    AgentTuiEventScope Scope);

internal sealed record AgentTuiInteractionHandlerRegistration(
    Interactions.IAgentTuiInteractionHandler Handler,
    AgentTuiEventScope Scope);
