using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal sealed class ExecuteCommandAutoBackgroundedTuiHandler : ExecuteCommandTuiHandlerBase<ExecuteCommandAutoBackgroundedEvent>
{
    public override ValueTask HandleAsync(
        ExecuteCommandAutoBackgroundedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var store = GetStore(context);
        var state = store.GetOrCreate(evt);
        state.IsBackground = true;
        state.BackgroundTaskId = evt.BackgroundTaskId;
        state.BackgroundedAt = evt.BackgroundedAt;
        state.DurationMilliseconds = evt.ElapsedMilliseconds;
        state.DisplayState = CodingCommandDisplayState.Backgrounded;
        store.IndexBackgroundTask(state);

        UpdateTranscript(context, state, evt);
        return ValueTask.CompletedTask;
    }
}
