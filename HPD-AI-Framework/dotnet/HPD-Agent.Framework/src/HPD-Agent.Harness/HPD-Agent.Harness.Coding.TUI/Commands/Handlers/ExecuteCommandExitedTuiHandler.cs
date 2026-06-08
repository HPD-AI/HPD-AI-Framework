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

        state.Artifacts.StdoutArtifactPath = evt.StdoutArtifactPath;
        state.Artifacts.StderrArtifactPath = evt.StderrArtifactPath;
        state.Artifacts.CombinedOutputArtifactPath = evt.CombinedOutputArtifactPath;
        state.Artifacts.StdoutContentId = evt.StdoutContentId;
        state.Artifacts.StderrContentId = evt.StderrContentId;
        state.Artifacts.CombinedOutputContentId = evt.CombinedOutputContentId;
        state.Artifacts.StdoutLocalPath = evt.StdoutLocalPath;
        state.Artifacts.StderrLocalPath = evt.StderrLocalPath;
        state.Artifacts.CombinedOutputLocalPath = evt.CombinedOutputLocalPath;

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
