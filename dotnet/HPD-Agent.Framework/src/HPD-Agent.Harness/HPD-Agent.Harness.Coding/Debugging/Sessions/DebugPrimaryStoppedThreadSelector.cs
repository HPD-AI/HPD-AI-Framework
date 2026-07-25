namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Selects the single semantic primary thread used by every stopped-state projection.</summary>
internal static class DebugPrimaryStoppedThreadSelector
{
    public static DebugThreadSnapshot? Select(
        DebugSessionState state,
        int? adapterReportedThreadId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var stopped = state.Threads.Where(thread => thread.IsStopped).ToArray();
        if (adapterReportedThreadId is { } reported)
        {
            var selected = stopped.SingleOrDefault(thread => thread.ThreadId == reported);
            if (selected is not null)
                return selected;
        }

        if (state.PrimaryStoppedThreadId is { } primary)
        {
            var selected = stopped.SingleOrDefault(thread => thread.ThreadId == primary);
            if (selected is not null)
                return selected;
        }

        return stopped.Length == 1
            ? stopped[0]
            : stopped.OrderBy(thread => thread.ThreadId).FirstOrDefault();
    }
}
