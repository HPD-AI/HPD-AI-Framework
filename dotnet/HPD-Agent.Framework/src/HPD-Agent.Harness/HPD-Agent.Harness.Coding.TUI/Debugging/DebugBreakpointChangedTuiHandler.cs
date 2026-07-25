using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugBreakpointChangedTuiHandler
    : AgentTuiEventHandler<DebugBreakpointChangedEvent>
{
    public override ValueTask HandleAsync(
        DebugBreakpointChangedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var state = context.State.GetOrCreate(
            DebugTuiState.StateKey,
            static () => new DebugTuiState());
        if (!state.BeginBreakpointProjection(evt))
            return ValueTask.CompletedTask;
        var selection = state.Reconcile(evt);
        if (selection is null) return ValueTask.CompletedTask;
        DebugBreakpointSelectionTuiHandler.Render(context, selection, evt);
        return ValueTask.CompletedTask;
    }
}
