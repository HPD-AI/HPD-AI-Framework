using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Application;

public sealed class AgentTuiSessionState
{
    private readonly HpdAgentTuiRegistry _registry;
    private readonly AgentTuiStateBag _state = new();
    private readonly Action? _requestRender;

    public AgentTuiSessionState(
        AgentTuiRuntimeScope scope,
        HpdAgentTuiRegistry registry,
        Action? requestRender = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _requestRender = requestRender;
        Shell = new ChatShellModel(scope);
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiStateBag State => _state;

    public async ValueTask ApplyEventAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default,
        AgentTuiEventDeliveryMode deliveryMode = AgentTuiEventDeliveryMode.Live)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var context = new AgentTuiEventContext(
            Scope,
            Shell,
            Shell.Navigation,
            _registry,
            _state,
            deliveryMode,
            _requestRender);
        foreach (var handler in _registry.FindEventHandlers(evt, Scope))
        {
            await handler.Value.HandleAsync(evt, context, cancellationToken)
                .ConfigureAwait(false);
        }
    }

}
