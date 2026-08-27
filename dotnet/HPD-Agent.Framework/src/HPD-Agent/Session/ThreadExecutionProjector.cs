namespace HPD.Agent;

/// <summary>Defines stable thread-execution projection states.</summary>
public static class ThreadExecutionStatus
{
    /// <summary>The execution remains live.</summary>
    public const string Active = "active";
    /// <summary>The execution completed successfully.</summary>
    public const string Succeeded = "succeeded";
    /// <summary>The execution was cancelled.</summary>
    public const string Cancelled = "cancelled";
    /// <summary>The execution failed.</summary>
    public const string Failed = "failed";
    /// <summary>The execution is no longer live and has no terminal fact.</summary>
    public const string Interrupted = "interrupted";
}

/// <summary>Contains one immutable execution projection and its unified operations.</summary>
public sealed record ThreadExecutionProjection(
    string ThreadExecutionId,
    string AgentId,
    string SessionId,
    string ThreadId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ThreadExecutionProjectionError? Error,
    IReadOnlyList<AgentOperationSnapshot> Operations);

/// <summary>Contains sanitized execution failure details.</summary>
public sealed record ThreadExecutionProjectionError(string? Type, string? Message);

/// <summary>Folds canonical execution and operation journal facts.</summary>
public static class ThreadExecutionProjector
{
    /// <summary>Reports whether an event participates in execution projection.</summary>
    public static bool IsProjectionEvent(AgentEvent evt) => evt is
        ThreadExecutionStartedEvent or ThreadExecutionFinishedEvent or
        AgentOperationRegisteredEvent or AgentOperationTransitionedEvent or
        AgentOperationTombstonedEvent or AgentOperationTombstoneEvictedEvent;

    /// <summary>Projects ordered journal facts for one agent thread.</summary>
    public static IReadOnlyList<ThreadExecutionProjection> Project(
        string agentId,
        string sessionId,
        string threadId,
        IEnumerable<AgentEvent> events,
        string? activeThreadExecutionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(events);
        var runs = new List<Builder>();
        foreach (var evt in events.OrderBy(static evt => evt.ThreadSequenceNumber))
        {
            switch (evt)
            {
                case ThreadExecutionStartedEvent started when started.AgentId == agentId:
                    runs.Add(new Builder(started, sessionId, threadId));
                    break;
                case ThreadExecutionFinishedEvent finished when finished.AgentId == agentId:
                    runs.LastOrDefault(run => run.ThreadExecutionId == finished.ThreadExecutionId)?.Complete(finished);
                    break;
                case AgentOperationRegisteredEvent registered:
                    FindRun(runs, registered.ThreadExecutionId, agentId)?.SetOperation(registered.Operation);
                    break;
                case AgentOperationTransitionedEvent transitioned:
                    FindRun(runs, transitioned.ThreadExecutionId, agentId)?.SetOperation(transitioned.Operation);
                    break;
                case AgentOperationTombstonedEvent tombstoned:
                    FindRun(runs, tombstoned.ThreadExecutionId, agentId)
                        ?.RemoveOperation(tombstoned.Tombstone.OperationId);
                    break;
            }
        }
        foreach (var run in runs)
            run.MarkInterruptedIfNotLive(activeThreadExecutionId);
        return runs.Select(static run => run.Build()).ToArray();
    }

    private static Builder? FindRun(IReadOnlyList<Builder> runs, string? executionId, string agentId) =>
        !string.IsNullOrWhiteSpace(executionId)
            ? runs.LastOrDefault(run => run.ThreadExecutionId == executionId)
            : runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);

    private sealed class Builder
    {
        private readonly string _sessionId;
        private readonly string _threadId;
        private readonly Dictionary<string, AgentOperationSnapshot> _operations = new(StringComparer.Ordinal);
        internal Builder(ThreadExecutionStartedEvent started, string sessionId, string threadId)
        {
            ThreadExecutionId = started.ThreadExecutionId;
            AgentId = started.AgentId;
            StartedAt = started.StartedAt;
            _sessionId = sessionId;
            _threadId = threadId;
        }
        internal string ThreadExecutionId { get; }
        internal string AgentId { get; }
        internal DateTimeOffset StartedAt { get; }
        internal DateTimeOffset? FinishedAt { get; private set; }
        internal string Status { get; private set; } = ThreadExecutionStatus.Active;
        internal ThreadExecutionProjectionError? Error { get; private set; }
        internal void Complete(ThreadExecutionFinishedEvent finished)
        {
            FinishedAt = finished.FinishedAt;
            Status = finished.Outcome switch
            {
                ThreadExecutionOutcome.Failed => ThreadExecutionStatus.Failed,
                ThreadExecutionOutcome.Cancelled => ThreadExecutionStatus.Cancelled,
                _ => ThreadExecutionStatus.Succeeded
            };
            Error = finished.Error is null ? null : new(finished.Error.Type, finished.Error.Message);
        }
        internal void SetOperation(AgentOperationSnapshot operation)
        {
            if (!_operations.TryGetValue(operation.OperationId, out var current) || operation.Version > current.Version)
                _operations[operation.OperationId] = operation;
        }
        internal void RemoveOperation(string operationId) => _operations.Remove(operationId);
        internal void MarkInterruptedIfNotLive(string? activeExecutionId)
        {
            if (Status == ThreadExecutionStatus.Active && ThreadExecutionId != activeExecutionId)
                Status = ThreadExecutionStatus.Interrupted;
        }
        internal ThreadExecutionProjection Build() => new(
            ThreadExecutionId, AgentId, _sessionId, _threadId, Status, StartedAt, FinishedAt, Error,
            _operations.Values.OrderBy(static operation => operation.RegisteredAt)
                .ThenBy(static operation => operation.OperationId, StringComparer.Ordinal).ToArray());
    }
}
