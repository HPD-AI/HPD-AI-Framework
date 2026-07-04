namespace HPD.Agent;

public static class ThreadEventStoreExtensions
{
    public static Task SaveInitialThreadAsync(
        this ISessionStore store,
        string sessionId,
        Thread thread,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(thread);

        return SaveAsync();

        async Task SaveAsync()
        {
            var document = ThreadEventDocumentBuilder.FromInitialThread(sessionId, thread);
            foreach (var evt in document.Events)
            {
                await store.AppendThreadEventAsync(
                    sessionId,
                    thread.Id,
                    evt,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static Task AppendThreadUpdatedAsync(
        this ISessionStore store,
        Thread thread,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(thread);

        return store.AppendThreadEventAsync(
            thread.SessionId,
            thread.Id,
            ThreadEventFactory.ThreadUpdated(thread),
            cancellationToken: cancellationToken);
    }
}
