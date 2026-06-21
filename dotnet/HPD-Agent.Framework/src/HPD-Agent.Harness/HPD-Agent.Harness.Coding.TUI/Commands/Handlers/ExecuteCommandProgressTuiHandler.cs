using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal sealed class ExecuteCommandProgressTuiHandler : ExecuteCommandTuiHandlerBase<ExecuteCommandProgressEvent>
{
    public override ValueTask HandleAsync(
        ExecuteCommandProgressEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetExistingState(context, evt, out var state) || !state.IsActive)
        {
            return ValueTask.CompletedTask;
        }

        state.ApplyBase(evt);
        state.DurationMilliseconds = evt.ElapsedMilliseconds;
        state.StdoutBytes = evt.StdoutBytes;
        state.StderrBytes = evt.StderrBytes;
        state.CombinedOutputBytes = evt.CombinedOutputBytes;
        state.CombinedBytesDiscarded = evt.CombinedBytesDiscarded;
        state.OutputObserved = evt.OutputObserved;
        state.OutputEventsSuppressed |= evt.OutputEventsSuppressed;

        UpdateTranscript(context, state, evt);
        return ValueTask.CompletedTask;
    }
}
