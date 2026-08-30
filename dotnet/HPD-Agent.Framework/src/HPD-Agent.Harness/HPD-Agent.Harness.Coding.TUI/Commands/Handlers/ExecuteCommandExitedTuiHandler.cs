using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal sealed class ExecuteCommandExitedTuiHandler : ExecuteCommandTuiHandlerBase<ExecuteCommandProcessExitedEvent>
{
    public override ValueTask HandleAsync(
        ExecuteCommandProcessExitedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var store = GetStore(context);
        var state = store.GetOrCreate(evt);
        if (state.CompletedAt.HasValue && !state.IsActive)
        {
            return ValueTask.CompletedTask;
        }

        state.CompletedAt = DateTimeOffset.UtcNow;
        state.ExitCode = evt.ExitCode;
        state.CompletionKind = evt.CompletionKind;
        state.DurationMilliseconds = evt.DurationMilliseconds;
        state.StdoutBytes = evt.StdoutBytes;
        state.StderrBytes = evt.StderrBytes;
        state.CombinedOutputBytes = evt.CombinedOutputBytes;
        state.CombinedBytesDiscarded = evt.CombinedBytesDiscarded;
        state.OutputTruncated |= evt.OutputTruncated;
        state.OutputEventsSuppressed |= evt.OutputEventsSuppressed;
        state.DrainTimedOut = evt.OutputDrainTimedOut;
        state.DisplayState = ResolveDisplayState(evt);

        state.Artifacts.StdoutContentId = evt.Stdout?.ContentId;
        state.Artifacts.StderrContentId = evt.Stderr?.ContentId;
        state.Artifacts.CombinedOutputContentId = evt.CombinedOutput?.ContentId;

        UpdateTranscript(context, state, evt);
        return ValueTask.CompletedTask;
    }

    private static CodingCommandDisplayState ResolveDisplayState(ExecuteCommandProcessExitedEvent evt)
        => evt.CompletionKind switch
        {
            ExecuteCommandCompletionKind.Completed when evt.ExitCode is 0 => CodingCommandDisplayState.Completed,
            ExecuteCommandCompletionKind.Completed => CodingCommandDisplayState.Failed,
            ExecuteCommandCompletionKind.TimedOut => CodingCommandDisplayState.TimedOut,
            ExecuteCommandCompletionKind.Cancelled or ExecuteCommandCompletionKind.Stopped => CodingCommandDisplayState.Cancelled,
            ExecuteCommandCompletionKind.FailedToStart or ExecuteCommandCompletionKind.Faulted => CodingCommandDisplayState.Failed,
            _ => CodingCommandDisplayState.Exited
        };
}
