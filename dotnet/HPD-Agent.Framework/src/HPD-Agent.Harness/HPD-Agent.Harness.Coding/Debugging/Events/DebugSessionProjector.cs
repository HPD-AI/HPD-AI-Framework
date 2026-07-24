using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record DebugProjectedArtifact(
    string DebugSessionId, string? ContentScope, string? ContentId, string? ContentVersion,
    long FirstSequence, long LastSequence);

public sealed record DebugProjectedSession
{
    public required string DebugSessionId { get; init; }
    public string? ParentDebugSessionId { get; set; }
    public required string AdapterId { get; init; }
    public string Status { get; set; } = "Started";
    public DebugSemanticStartKind SemanticStartKind { get; set; }
    public DebugAdapterStartMethod AdapterStartMethod { get; set; }
    public int? ExitCode { get; set; }
    public int? StoppedThreadId { get; set; }
    public string? StopReason { get; set; }
    public bool RestartRequested { get; set; }
    public DebugSessionSummaryEvent? FinalSummary { get; set; }
    public List<DebugProjectedBreakpoint> BreakpointHistory { get; } = [];
}

public sealed record DebugProjectedBreakpoint(
    string Reason, int? BreakpointId, bool Verified, string? SourcePath, long? Line,
    long? Column, string? InstructionReference);

public sealed record DebugProjectedTree
{
    public required string DebugTreeId { get; init; }
    public string? EnvironmentId { get; set; }
    public string Status { get; set; } = "Started";
    public Dictionary<string, DebugProjectedSession> Sessions { get; } = new(StringComparer.Ordinal);
    public List<DebugProjectedArtifact> Artifacts { get; } = [];
}

public sealed record DebugProjection(IReadOnlyDictionary<string, DebugProjectedTree> Trees);

/// <summary>Reconstructs durable debugger history without depending on ephemeral live projections.</summary>
public static class DebugSessionProjector
{
    public static DebugProjection Project(IEnumerable<AgentEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var trees = new Dictionary<string, DebugProjectedTree>(StringComparer.Ordinal);
        foreach (var @event in events.OrderBy(x => x.ThreadSequenceNumber == 0 ? long.MaxValue : x.ThreadSequenceNumber))
            Apply(trees, @event);
        return new(trees);
    }

    public static bool IsProjectionEvent(AgentEvent @event) => @event is DebugLifecycleEvent;

    private static void Apply(Dictionary<string, DebugProjectedTree> trees, AgentEvent @event)
    {
        if (@event is not DebugLifecycleEvent debug) return;
        if (!trees.TryGetValue(debug.DebugTreeId, out var tree))
            trees.Add(debug.DebugTreeId, tree = new() { DebugTreeId = debug.DebugTreeId });
        var session = GetSession(tree, debug);
        switch (debug)
        {
            case DebugTreeStartedEvent started:
                tree.EnvironmentId = started.EnvironmentId;
                tree.Status = "Running";
                session.SemanticStartKind = started.SemanticStartKind;
                session.AdapterStartMethod = started.AdapterStartMethod;
                break;
            case DebugChildSessionStartedEvent child:
                session.ParentDebugSessionId = child.ParentDebugSessionId;
                session.AdapterStartMethod = child.AdapterStartMethod;
                break;
            case DebugSessionStoppedEvent stopped:
                session.Status = "Stopped";
                session.StoppedThreadId = stopped.AdapterThreadId;
                session.StopReason = stopped.Reason;
                break;
            case DebugSessionContinuedEvent:
                session.Status = "Running";
                session.StoppedThreadId = null;
                session.StopReason = null;
                break;
            case DebugSessionExitedEvent exited:
                session.ExitCode = exited.ExitCode;
                break;
            case DebugSessionTerminatedEvent terminated:
                session.Status = "Terminated";
                session.RestartRequested = terminated.RestartRequested;
                break;
            case DebugSessionFailedEvent:
                session.Status = "Faulted";
                break;
            case DebugBreakpointChangedEvent breakpoint:
                session.BreakpointHistory.Add(new(breakpoint.Reason, breakpoint.BreakpointId,
                    breakpoint.Verified, breakpoint.SourcePath, breakpoint.Line, breakpoint.Column,
                    breakpoint.InstructionReference));
                break;
            case DebugSessionSummaryEvent summary:
                session.FinalSummary = summary;
                session.Status = summary.FinalStatus;
                session.ExitCode = summary.ExitCode;
                break;
            case DebugTreeFaultedEvent:
                tree.Status = "Faulted";
                break;
            case DebugTreeTerminatedEvent:
                tree.Status = "Terminated";
                break;
            case DebugOutputAvailableEvent output when output.ContentId is not null:
                tree.Artifacts.Add(new(output.DebugSessionId, output.ContentScope, output.ContentId,
                    output.ContentVersion, output.FirstSequence, output.LastSequence));
                break;
        }
    }

    private static DebugProjectedSession GetSession(DebugProjectedTree tree, DebugLifecycleEvent @event)
    {
        if (!tree.Sessions.TryGetValue(@event.DebugSessionId, out var session))
            tree.Sessions.Add(@event.DebugSessionId, session = new()
            {
                DebugSessionId = @event.DebugSessionId,
                AdapterId = @event.AdapterId
            });
        return session;
    }
}
