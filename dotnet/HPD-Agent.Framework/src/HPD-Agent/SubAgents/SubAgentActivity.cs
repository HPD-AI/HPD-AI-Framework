namespace HPD.Agent;

/// <summary>Execution activity and result submission for one child, independent of addressability.</summary>
public sealed record SubAgentActivity(string? ExecutionId, string Status, string? Report)
{
    public int UserQuestionCount { get; init; }
    public int ParentQuestionCount { get; init; }
    public int OtherRequestCount { get; init; }
}

/// <summary>Canonical child status used by hosted and local clients.</summary>
public static class SubAgentActivityReader
{
    public static Task<SubAgentActivity> ReadAsync(ISessionStore store, ThreadKey child,
        CancellationToken cancellationToken = default) => ReadCoreAsync(store, child, null, cancellationToken);

    internal static Task<SubAgentActivity> ReadExecutionAsync(ISessionStore store, ThreadKey child,
        string execution, CancellationToken cancellationToken) => ReadCoreAsync(store, child, execution, cancellationToken);

    private static async Task<SubAgentActivity> ReadCoreAsync(ISessionStore store, ThreadKey child,
        string? selectedExecution, CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(child, cancellationToken).ConfigureAwait(false);
        if (head is null) return new(null, "unavailable", null);
        var events = await SubAgentResults.ReadAsync(store, child, head.Cursor, cancellationToken).ConfigureAwait(false);
        var live = await ThreadExecutionControllerRegistry.For(store).FindActiveAsync(child, cancellationToken)
            .ConfigureAwait(false);
        return Project(events, live.ThreadExecutionId, selectedExecution);
    }

    internal static SubAgentActivity Project(IReadOnlyList<AgentEvent> events, string? liveExecutionId, string? selectedExecution = null)
    {
        var execution = selectedExecution ?? events.OfType<ThreadExecutionStartedEvent>().LastOrDefault()?.ThreadExecutionId;
        if (execution is null) return new(null, "idle", null);
        var terminal = events.OfType<ThreadExecutionFinishedEvent>().LastOrDefault(e => e.ThreadExecutionId == execution);
        var report = events.OfType<SubAgentResultSubmittedEvent>().LastOrDefault(e => e.ExecutionId == execution)?.Report;
        if (terminal is not null)
            return new(execution, terminal.Outcome switch
            {
                ThreadExecutionOutcome.Failed => "failed",
                ThreadExecutionOutcome.Cancelled => "cancelled",
                _ => report is null ? "stopped without result" : "completed"
            }, report);
        // A durable start alone cannot establish runtime liveness (e.g. after a process restart).
        if (liveExecutionId != execution) return new(execution, "runtime unavailable", report);
        if (report is not null) return new(execution, "finishing", report);
        var pending = AgentRequestProjector.ProjectPending(events, execution);
        var parentQuestions = pending.OfType<ParentQuestionRequestEvent>().Count();
        var userQuestions = pending.OfType<UserQuestionRequestEvent>().Count();
        return new(execution, parentQuestions > 0 ? "waiting for parent" : userQuestions > 0 ? "waiting for user" : pending.Count > 0 ? "waiting for input" : "running", null)
        { ParentQuestionCount = parentQuestions, UserQuestionCount = userQuestions, OtherRequestCount = pending.Count - parentQuestions - userQuestions };
    }
}
