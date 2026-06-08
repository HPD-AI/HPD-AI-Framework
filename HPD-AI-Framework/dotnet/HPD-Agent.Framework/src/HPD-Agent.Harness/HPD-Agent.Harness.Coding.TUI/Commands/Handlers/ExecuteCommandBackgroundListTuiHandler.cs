using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal sealed class ExecuteCommandBackgroundListTuiHandler : ExecuteCommandTuiHandlerBase<ExecuteCommandBackgroundListEvent>
{
    public override ValueTask HandleAsync(
        ExecuteCommandBackgroundListEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var store = GetStore(context);
        if (!store.TryGetByCommandId(evt.CommandId, out var state))
        {
            return ValueTask.CompletedTask;
        }

        state.ApplyBase(evt);
        state.IsBackground = true;
        state.DisplayState = evt.Count > 0
            ? CodingCommandDisplayState.Backgrounded
            : state.DisplayState;

        UpdateTranscript(context, state, evt);
        return ValueTask.CompletedTask;
    }
}
