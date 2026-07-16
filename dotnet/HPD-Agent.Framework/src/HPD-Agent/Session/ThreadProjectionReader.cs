namespace HPD.Agent;

public enum ThreadProjectionPurpose
{
    ModelContext,
    ThreadHistory,
    Evaluation,
    ForkConstruction,
    CompleteSemanticExport
}

/// <summary>Explicit, potentially expensive projections over the canonical thread journal.</summary>
public static class ThreadProjectionReader
{
    public static async ValueTask<Thread?> ProjectThreadAsync(
        this ISessionStore store,
        string sessionId,
        string threadId,
        ThreadProjectionPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var key = new ThreadKey(sessionId, threadId);
        if (await store.GetThreadAsync(key, cancellationToken).ConfigureAwait(false) is null)
            return null;

        var thread = new Thread(sessionId, threadId);
        await foreach (var batch in store.ReadThreadEventsAsync(
            key,
            new ThreadEventReadRequest(),
            cancellationToken).ConfigureAwait(false))
            ThreadProjector.Apply(thread, batch.Events, purpose);
        thread.Session = await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return thread;
    }

    public static async ValueTask<IReadOnlyList<AgentEvent>?> CollectThreadEventsAsync(
        this ISessionStore store,
        ThreadKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (await store.GetThreadAsync(key, cancellationToken).ConfigureAwait(false) is null)
            return null;

        var events = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            key,
            new ThreadEventReadRequest(),
            cancellationToken).ConfigureAwait(false))
            events.AddRange(batch.Events);
        return events;
    }

    public static ValueTask<IReadOnlyList<AgentEvent>?> CollectThreadEventsAsync(
        this ISessionStore store,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
        => store.CollectThreadEventsAsync(new ThreadKey(sessionId, threadId), cancellationToken);

    public static async ValueTask<IReadOnlyList<ThreadDescriptor>> CollectThreadDescriptorsAsync(
        this ISessionStore store,
        string sessionId,
        ThreadListRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var descriptors = new List<ThreadDescriptor>();
        await foreach (var descriptor in store.ListThreadsAsync(
            sessionId,
            request ?? new ThreadListRequest(),
            cancellationToken).ConfigureAwait(false))
            descriptors.Add(descriptor);
        return descriptors;
    }
}
