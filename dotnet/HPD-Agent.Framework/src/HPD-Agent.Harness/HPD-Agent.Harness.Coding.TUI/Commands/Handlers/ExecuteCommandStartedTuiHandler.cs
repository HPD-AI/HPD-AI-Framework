using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal sealed class ExecuteCommandStartedTuiHandler : ExecuteCommandTuiHandlerBase<ExecuteCommandProcessStartedEvent>
{
    public override ValueTask HandleAsync(
        ExecuteCommandProcessStartedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var store = GetStore(context);
        var state = store.GetOrCreate(evt);
        state.Shell = evt.Shell;
        state.StartedAt = evt.StartedAt;
        state.IsBackground = evt.Background;
        state.OperationId = evt.Background ? evt.CommandId : state.OperationId;
        state.BackgroundedAt = evt.Background ? evt.StartedAt : state.BackgroundedAt;
        state.AutoBackgroundEligible = evt.AutoBackgroundEligible;
        state.ProcessId = evt.ProcessId;
        state.TimeoutMilliseconds = evt.TimeoutMilliseconds;
        state.DisplayState = evt.Background
            ? CodingCommandDisplayState.Backgrounded
            : CodingCommandDisplayState.Running;
        store.IndexOperation(state);

        UpdateTranscript(context, state, evt);
        return ValueTask.CompletedTask;
    }
}
