namespace HPD.Agent;

/// <summary>Creates and persists sessions and threads without requiring an <see cref="Agent"/>.</summary>
public static class SessionStoreLifecycleExtensions
{
    /// <summary>
    /// Creates a session with a new or caller-selected ID.
    /// </summary>
    /// <exception cref="InvalidOperationException">A session with the same ID already exists.</exception>
    public static async ValueTask<Session> CreateSessionAsync(
        this ISessionStore store,
        string? sessionId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var id = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString()
            : sessionId;
        var session = new Session(id)
        {
            Store = store
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
                session.AddMetadata(key, value);
        }

        if (!await store.TryCreateSessionAsync(session, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Session '{id}' already exists. Session IDs must be unique; " +
                "use a different ID or load the existing session.");
        }

        return session;
    }

    /// <summary>
    /// Creates an empty thread in an existing session.
    /// </summary>
    /// <param name="store">The session store that owns the session.</param>
    /// <param name="sessionId">The existing session identifier.</param>
    /// <param name="defaultAgentId">The agent selected when the thread is resumed without an explicit agent.</param>
    /// <param name="threadId">Optional thread identifier. When omitted, a GUID is generated.</param>
    /// <param name="name">Optional display name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="SessionNotFoundException">The session does not exist.</exception>
    /// <exception cref="InvalidOperationException">A thread with the same ID already exists.</exception>
    public static async ValueTask<Thread> CreateThreadAsync(
        this ISessionStore store,
        string sessionId,
        string defaultAgentId,
        string? threadId = null,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultAgentId);

        var session = await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new SessionNotFoundException(sessionId);
        session.Store = store;

        var thread = session.CreateThread(defaultAgentId, threadId);
        if (!string.IsNullOrWhiteSpace(name))
            thread.Name = name;

        try
        {
            await store.SaveInitialThreadAsync(sessionId, thread, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ThreadAppendConflictException)
        {
            throw new InvalidOperationException(
                $"Thread '{thread.Id}' already exists in session '{sessionId}'.");
        }

        session.LastActivity = thread.LastActivity;
        await store.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        return thread;
    }
}
