namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal static class CodingCommandRenderText
{
    public static string VerbFor(CodingCommandExecutionState state)
        => state.DisplayState switch
        {
            CodingCommandDisplayState.Running => "Running",
            CodingCommandDisplayState.Backgrounded => "Backgrounded",
            CodingCommandDisplayState.Completed => "Ran",
            CodingCommandDisplayState.Failed => "Failed",
            CodingCommandDisplayState.Cancelled => "Cancelled",
            CodingCommandDisplayState.TimedOut => "Timed out",
            _ => "Exited"
        };

    public static bool ShouldRenderSummary(CodingCommandExecutionState state)
        => state.DisplayState != CodingCommandDisplayState.Running ||
            state.OutputTruncated ||
            state.OutputEventsSuppressed ||
            state.BinaryOutputObserved ||
            state.Artifacts.HasAny;

    public static string BuildSummary(CodingCommandExecutionState state)
    {
        var parts = new List<string>();
        if (state.DisplayState == CodingCommandDisplayState.Backgrounded)
        {
            parts.Add(state.BackgroundTaskId is null ? "background" : $"background {state.BackgroundTaskId}");
        }

        if (state.DurationMilliseconds is { } duration &&
            state.DisplayState != CodingCommandDisplayState.Running)
        {
            parts.Add(FormatDuration(duration));
        }

        if (state.ExitCode is { } exitCode)
        {
            parts.Add($"exit {exitCode}");
        }
        else if (state.CompletionKind is { } kind && kind != ExecuteCommandCompletionKind.Completed)
        {
            parts.Add(kind.ToString());
        }

        if (state.OutputTruncated)
        {
            parts.Add("output truncated");
        }

        if (state.OutputEventsSuppressed)
        {
            parts.Add("output suppressed");
        }

        if (state.BinaryOutputObserved)
        {
            parts.Add("binary output");
        }

        if (state.Artifacts.HasAny)
        {
            parts.Add("artifacts");
        }

        return parts.Count == 0 ? "running" : string.Join(" | ", parts);
    }

    public static string FormatDuration(long milliseconds)
    {
        if (milliseconds < 1_000)
        {
            return $"{milliseconds}ms";
        }

        var seconds = TimeSpan.FromMilliseconds(milliseconds).TotalSeconds;
        return $"{seconds:0.0}s";
    }
}
