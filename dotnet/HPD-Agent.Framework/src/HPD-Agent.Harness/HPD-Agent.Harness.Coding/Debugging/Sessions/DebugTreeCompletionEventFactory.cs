using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal static class DebugTreeCompletionEventFactory
{
    public static DebugTreeCompletedEvent Create(
        DebugSessionTree tree,
        DebugSession root,
        string finalStatus,
        string safeReasonCode)
    {
        var desired = tree.Breakpoints.Snapshot;
        var bindings = root.AdapterBreakpoints.Snapshot;
        var requested = desired.Source.Length + desired.Function.Length +
            desired.Exception.Length + desired.Instruction.Length + desired.Data.Length;
        var acknowledged = bindings.Count(item => item.Acknowledged);
        var verified = bindings.Count(item => item.Verified);
        var hits = root.AdapterBreakpoints.HitCounts;
        var sessions = tree.Sessions.Values.ToArray();
        var outputs = sessions.Select(session => session.Output.Snapshot(includeTelemetry: true)).ToArray();
        return new()
        {
            SessionId = tree.Ownership.SessionId,
            ThreadId = tree.Ownership.ThreadId,
            TraceId = tree.RuntimeBinding.EventScope.TraceId,
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = root.SessionId,
            AdapterId = root.AdapterPlan.AdapterId,
            ToolCallId = tree.RuntimeBinding.EventScope.ToolCallId,
            FinalStatus = finalStatus,
            ExitCode = root.ExitCode,
            DurationMilliseconds = Math.Max(
                0,
                (long)(DateTimeOffset.UtcNow - root.CreatedAt).TotalMilliseconds),
            SessionCount = sessions.Length,
            ChildSessionCount = Math.Max(0, sessions.Length - 1),
            Breakpoints = new(
                requested,
                acknowledged,
                verified,
                Math.Max(0, requested - verified),
                hits.Hit,
                hits.Unknown),
            BreakpointStopCount = root.AdapterBreakpoints.BreakpointStopCount,
            RetainedOutputBytes = outputs.Sum(output => output.RetainedBytes),
            DroppedOutputRecords = outputs.Sum(output => output.DroppedRecords),
            DroppedOutputBytes = outputs.Sum(output => output.DroppedBytes),
            ProjectionFailures = sessions.Sum(session => session.Projections.FollowUpFailures),
            SafeReasonCode = safeReasonCode
        };
    }
}
