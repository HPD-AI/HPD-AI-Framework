using System.Collections.Concurrent;


namespace HPD.Agent;

/// <summary>
/// In-memory session store for development and testing.
/// V3 Architecture: Separate storage for Session metadata and thread event documents.
/// Data is lost on process restart.
/// </summary>
/// <remarks>
/// <para><b>Storage Structure:</b></para>
/// <code>
/// _sessions: ConcurrentDictionary&lt;string, Session&gt;        ← Session metadata
/// _threads: ConcurrentDictionary&lt;string, ThreadEventDocument&gt; ← Event documents per thread
/// </code>
/// </remarks>
public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ThreadEventDocument>> _threads = new();

    // ═══════════════════════════════════════════════════════════════════
    // SESSION PERSISTENCE ( Metadata only)
    // ═══════════════════════════════════════════════════════════════════

    public Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<Session?>(session);
        }

        return Task.FromResult<Session?>(null);
    }

    public Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_sessions.Keys.ToList());
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        _threads.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // THREAD EVENT PERSISTENCE
    // ═══════════════════════════════════════════════════════════════════

    public Task<Thread?> LoadThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (_threads.TryGetValue(sessionId, out var sessionThreads) &&
            sessionThreads.TryGetValue(threadId, out var document))
        {
            var thread = ThreadProjector.Project(document);
            if (_sessions.TryGetValue(sessionId, out var session))
                thread.Session = session;
            return Task.FromResult<Thread?>(thread);
        }

        return Task.FromResult<Thread?>(null);
    }

    public Task<ThreadEventDocument?> LoadThreadDocumentAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (_threads.TryGetValue(sessionId, out var sessionThreads) &&
            sessionThreads.TryGetValue(threadId, out var document))
        {
            return Task.FromResult<ThreadEventDocument?>(document);
        }

        return Task.FromResult<ThreadEventDocument?>(null);
    }

    public Task AppendThreadEventAsync(
        string sessionId,
        string threadId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        cancellationToken.ThrowIfCancellationRequested();
        evt = ThreadEventValidation.PrepareForAppend(sessionId, threadId, evt);

        var sessionThreads = _threads.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, ThreadEventDocument>());
        sessionThreads.AddOrUpdate(
            threadId,
            _ =>
            {
                evt.SequenceNumber = 1;
                return new ThreadEventDocument
                {
                    SessionId = sessionId,
                    ThreadId = threadId,
                    CreatedAt = evt.Timestamp,
                    UpdatedAt = evt.Timestamp,
                    NextSequenceNumber = 2,
                    Events = [evt]
                };
            },
            (_, existing) =>
            {
                if (expectedSequenceNumber is not null &&
                    existing.NextSequenceNumber - 1 != expectedSequenceNumber.Value)
                {
                    throw new InvalidOperationException(
                        $"Thread '{threadId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {existing.NextSequenceNumber - 1}.");
                }

                evt.SequenceNumber = existing.NextSequenceNumber;
                var events = existing.Events.ToList();
                events.Add(evt);
                return existing with
                {
                    UpdatedAt = evt.Timestamp,
                    NextSequenceNumber = existing.NextSequenceNumber + 1,
                    Events = events
                };
            });

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEvent> ReadThreadEventsAsync(
        string sessionId,
        string threadId,
        HPD.Events.ReplayReadOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var document = await LoadThreadDocumentAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            yield break;

        await foreach (var evt in document.Events.FilterByReplayOptions(options, cancellationToken).ConfigureAwait(false))
            yield return evt;
    }

    public Task<List<string>> ListThreadIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_threads.TryGetValue(sessionId, out var sessionThreads))
        {
            return Task.FromResult(sessionThreads.Keys.ToList());
        }

        return Task.FromResult(new List<string>());
    }

    public Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (_threads.TryGetValue(sessionId, out var sessionThreads))
        {
            sessionThreads.TryRemove(threadId, out _);
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - inactivityThreshold;
        var sessionsToRemove = new List<string>();

        foreach (var kvp in _sessions)
        {
            if (kvp.Value.LastActivity < cutoff)
            {
                sessionsToRemove.Add(kvp.Key);
            }
        }

        if (!dryRun)
        {
            foreach (var sessionId in sessionsToRemove)
            {
                _sessions.TryRemove(sessionId, out _);
                _threads.TryRemove(sessionId, out _);
            }
        }

        return Task.FromResult(sessionsToRemove.Count);
    }
}
