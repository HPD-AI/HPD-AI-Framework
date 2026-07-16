namespace HPD.Agent;

public static class ThreadEventStoreExtensions
{
    public static async ValueTask<AgentEvent> AppendThreadEventAsync(
        this ISessionStore store,
        string sessionId,
        string threadId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(evt);

        var result = await store.AppendThreadEventsAsync(
            new ThreadKey(sessionId, threadId),
            [evt],
            new ThreadAppendCondition(expectedSequenceNumber),
            cancellationToken).ConfigureAwait(false);

        return result.CommittedEvents[0];
    }

    public static async Task SaveInitialThreadAsync(
        this ISessionStore store,
        string sessionId,
        Thread thread,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(thread);

        var events = new List<AgentEvent>
        {
            ThreadEventFactory.ThreadCreated(thread)
        };

        foreach (var message in thread.Messages)
            events.AddRange(ThreadMessageEventConverter.ToThreadEvents(thread.SessionId, thread.Id, message));

        if (thread.MiddlewareState.Count > 0)
        {
            events.Add(ThreadEventFactory.ThreadMiddlewareStateCommitted(
                thread.SessionId,
                thread.Id,
                thread.MiddlewareState));
        }

        await store.AppendThreadEventsAsync(
            new ThreadKey(sessionId, thread.Id),
            events,
            new ThreadAppendCondition(ExpectedHead: 0),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task AppendThreadUpdatedAsync(
        this ISessionStore store,
        Thread thread,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(thread);

        await store.AppendThreadEventAsync(
            thread.SessionId,
            thread.Id,
            ThreadEventFactory.ThreadUpdated(thread),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
