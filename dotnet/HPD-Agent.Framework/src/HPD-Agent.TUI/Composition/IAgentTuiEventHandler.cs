using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiEventContext
{
    public AgentTuiEventContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        HpdAgentTuiRegistry registry,
        AgentTuiStateBag state,
        AgentTuiEventDeliveryMode deliveryMode = AgentTuiEventDeliveryMode.Live)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        State = state ?? throw new ArgumentNullException(nameof(state));
        DeliveryMode = deliveryMode;
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public HpdAgentTuiRegistry Registry { get; }

    public AgentTuiStateBag State { get; }

    public AgentTuiEventDeliveryMode DeliveryMode { get; }
}

public interface IAgentTuiEventHandler
{
    bool CanHandle(AgentEvent evt);

    ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Declares that an event handler provides the specialized transcript projection for selected tool calls.
/// Generic tool renderers can use this per-call ownership signal to avoid rendering the same call twice.
/// </summary>
public interface IAgentTuiToolCallHandler
{
    /// <summary>Returns whether this handler owns the specified tool call.</summary>
    bool CanHandleToolCall(
        string? toolHarnessName,
        string toolName,
        ToolCallType? callType);
}

public abstract class AgentTuiEventHandler<TEvent> : IAgentTuiEventHandler
    where TEvent : AgentEvent
{
    public bool CanHandle(AgentEvent evt) => evt is TEvent;

    public abstract ValueTask HandleAsync(
        TEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken);

    ValueTask IAgentTuiEventHandler.HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
        => evt is TEvent typed
            ? HandleAsync(typed, context, cancellationToken)
            : ValueTask.CompletedTask;
}
