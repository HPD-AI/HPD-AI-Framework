namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Re-encodes authoritative hosted control state when a hard compaction replaces a journal.
/// </summary>
public sealed class HostedThreadJournalRebaseSeedProvider : IThreadJournalRebaseSeedProvider
{
    private readonly SessionManager _sessions;

    public HostedThreadJournalRebaseSeedProvider(SessionManager sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = _sessions.GetActiveThreadExecution(thread.SessionId, thread.ThreadId);
        if (active is null)
            return [];

        var events = new List<AgentEvent>
        {
            new ThreadExecutionStartedEvent(active.ThreadExecutionId, active.AgentId, active.StartedAt)
            {
                SessionId = thread.SessionId,
                ThreadId = thread.ThreadId
            }
        };

        var journal = await _sessions.Store.CollectThreadEventsAsync(thread, cancellationToken)
            .ConfigureAwait(false) ?? [];
        events.AddRange(AgentRequestProjector
            .ProjectPending(journal, active.ThreadExecutionId)
            .Select(evt => evt with
            {
                EventId = Guid.NewGuid().ToString("N"),
                ThreadSequenceNumber = 0,
                SessionId = thread.SessionId,
                ThreadId = thread.ThreadId,
                Timestamp = DateTimeOffset.UtcNow
            }));

        return events;
    }
}
