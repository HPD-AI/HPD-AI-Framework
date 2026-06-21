using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal sealed class ExecuteCommandOutputChunkTuiHandler : ExecuteCommandTuiHandlerBase<ExecuteCommandOutputChunkEvent>
{
    public override ValueTask HandleAsync(
        ExecuteCommandOutputChunkEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetExistingState(context, evt, out var state) || !state.IsActive)
        {
            return ValueTask.CompletedTask;
        }

        state.ApplyBase(evt);
        state.OutputObserved = true;
        state.OutputTruncated |= evt.Truncated;
        state.OutputEventsSuppressed |= evt.Suppressed;
        state.BinaryOutputObserved |= evt.Binary;
        state.CombinedOutputBytes = evt.CombinedBytesObserved;
        if (evt.Stream == ExecuteCommandStreamKind.Stdout)
        {
            state.StdoutBytes = evt.StreamBytesObserved;
        }
        else
        {
            state.StderrBytes = evt.StreamBytesObserved;
        }

        state.Output.Append(evt.Stream, evt.Text, evt.Suppressed, evt.Binary, evt.Truncated);
        UpdateTranscript(context, state, evt);
        return ValueTask.CompletedTask;
    }
}
