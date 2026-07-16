namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Re-encodes authoritative hosted control state when a hard compaction replaces a journal.
/// </summary>
public sealed class HostedThreadJournalRebaseSeedProvider : IThreadJournalRebaseSeedProvider
{
    private readonly SessionManager _sessions;
    private readonly AgentManager _agents;

    public HostedThreadJournalRebaseSeedProvider(SessionManager sessions, AgentManager agents)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
    }

    public ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = _sessions.GetActiveThreadRun(thread.SessionId, thread.ThreadId);
        if (active is null)
            return ValueTask.FromResult<IReadOnlyList<AgentEvent>>([]);

        var events = new List<AgentEvent>
        {
            new ThreadRunStartedEvent(active.RuntimeRunId, active.AgentId, active.StartedAt)
            {
                SessionId = thread.SessionId,
                ThreadId = thread.ThreadId
            }
        };

        var coordinator = _agents
            .GetRuntimeAgent(active.AgentId, thread.SessionId, thread.ThreadId)?
            .EventCoordinator;
        if (coordinator is not null)
        {
            events.AddRange(coordinator.GetPendingRequests()
                .Select(static pending => pending.Request)
                .OfType<AgentEvent>()
                .Where(evt =>
                    string.Equals(evt.SessionId, thread.SessionId, StringComparison.Ordinal) &&
                    string.Equals(evt.ThreadId, thread.ThreadId, StringComparison.Ordinal))
                .Select(evt => evt with
                {
                    EventId = Guid.NewGuid().ToString("N"),
                    ThreadSequenceNumber = 0,
                    SessionId = thread.SessionId,
                    ThreadId = thread.ThreadId,
                    Timestamp = DateTimeOffset.UtcNow
                }));
        }

        return ValueTask.FromResult<IReadOnlyList<AgentEvent>>(events);
    }
}
