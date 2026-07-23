namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugSessionSnapshot(
    string DebugSessionId,
    string? ParentDebugSessionId,
    string AdapterId,
    DebugSessionStatus Status,
    bool IsAttach,
    int ThreadCount,
    int StoppedThreadCount,
    string? StopReason,
    DebugProcessSnapshot? Process,
    int ModuleCount,
    int LoadedSourceCount,
    long RetainedOutputBytes,
    long DroppedOutputRecords,
    long DroppedOutputBytes,
    long ProjectionFailures);

internal sealed record DebugTreeSnapshot(
    string DebugTreeId,
    string? ActiveDebugSessionId,
    string Status,
    IReadOnlyList<DebugSessionSnapshot> Sessions,
    int SessionCount,
    bool SessionsTruncated,
    int ChildSessionCount,
    long RetainedOutputBytes,
    long DroppedOutputRecords,
    long DroppedOutputBytes,
    long ProjectionFailures);

internal static class DebugSnapshotProjector
{
    public const int MaximumSessions = 128;

    public static DebugTreeSnapshot Project(DebugSessionTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var allSessions = tree.Sessions.Values
            .OrderBy(x => x.SessionId, StringComparer.Ordinal)
            .Select(Project)
            .ToArray();
        var sessions = allSessions.Take(MaximumSessions).ToArray();
        return new(
            tree.Ownership.DebugTreeId,
            tree.ActiveSessionId,
            AggregateStatus(allSessions.Select(x => x.Status)),
            sessions,
            allSessions.Length,
            allSessions.Length > sessions.Length,
            allSessions.Count(x => x.ParentDebugSessionId is not null),
            allSessions.Sum(x => x.RetainedOutputBytes),
            allSessions.Sum(x => x.DroppedOutputRecords),
            allSessions.Sum(x => x.DroppedOutputBytes),
            allSessions.Sum(x => x.ProjectionFailures));
    }

    private static DebugSessionSnapshot Project(DebugSession session)
    {
        var threads = session.State.Threads;
        var stopped = threads.Where(x => x.IsStopped).ToArray();
        var output = session.Output.Snapshot(includeTelemetry: true);
        return new(
            session.SessionId,
            session.ParentSessionId,
            session.LaunchPlan.AdapterId,
            session.State.Status,
            session.IsAttach,
            threads.Count,
            stopped.Length,
            stopped.Select(x => x.StopReason).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            session.Projections.Process,
            session.Projections.Modules.Count,
            session.Projections.Sources.Count,
            output.RetainedBytes,
            output.DroppedRecords,
            output.DroppedBytes,
            session.Projections.FollowUpFailures);
    }

    public static string AggregateStatus(IEnumerable<DebugSessionStatus> statuses)
    {
        var values = statuses.ToArray();
        if (values.Length == 0) return "Terminated";
        if (values.Any(x => x == DebugSessionStatus.Faulted)) return "Faulted";
        if (values.Any(x => x == DebugSessionStatus.Stopped)) return "Stopped";
        if (values.Any(x => x == DebugSessionStatus.PartiallyStopped)) return "PartiallyStopped";
        if (values.All(x => x == DebugSessionStatus.Terminated)) return "Terminated";
        if (values.Any(x => x is DebugSessionStatus.Initializing or DebugSessionStatus.Configuring))
            return "Starting";
        if (values.Any(x => x == DebugSessionStatus.Terminating)) return "Terminating";
        return "Running";
    }
}
