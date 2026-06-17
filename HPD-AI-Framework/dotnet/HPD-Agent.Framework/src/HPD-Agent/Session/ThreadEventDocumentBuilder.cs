namespace HPD.Agent;

public static class ThreadEventDocumentBuilder
{
    public static ThreadEventDocument FromInitialThread(string sessionId, Thread thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(thread);

        var createdAt = new DateTimeOffset(thread.CreatedAt, TimeSpan.Zero);
        var events = new List<AgentEvent>();
        if (thread.ForkedFrom is null)
        {
            events.Add(ThreadEventFactory.ThreadCreated(thread));
            if (!HasDefaultRootTreeState(thread))
                events.Add(ThreadEventFactory.ThreadTreeUpdated(thread));
        }
        else
        {
            events.Add(ThreadEventFactory.ThreadForked(thread));
            events.Add(ThreadEventFactory.ThreadMetadataUpdated(thread));
            events.Add(ThreadEventFactory.ThreadTreeUpdated(thread));
        }

        foreach (var message in thread.Messages)
            events.AddRange(ThreadMessageEventConverter.ToThreadEvents(thread.SessionId, thread.Id, message));

        if (thread.MiddlewareState.Count > 0)
        {
            events.Add(ThreadEventFactory.ThreadMiddlewareStateCommitted(
                thread.SessionId,
                thread.Id,
                thread.MiddlewareState));
        }

        return Create(
            sessionId,
            thread.Id,
            events,
            createdAt,
            new DateTimeOffset(thread.LastActivity, TimeSpan.Zero));
    }

    private static bool HasDefaultRootTreeState(Thread thread) =>
        thread.SiblingIndex == 0 &&
        thread.TotalSiblings == 1 &&
        thread.IsOriginal &&
        thread.OriginalThreadId is null &&
        thread.PreviousSiblingId is null &&
        thread.NextSiblingId is null &&
        thread.ChildThreads.Count == 0;

    public static ThreadEventDocument Create(
        string sessionId,
        string threadId,
        IEnumerable<AgentEvent> events,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var sequenced = new List<AgentEvent>();
        var sequence = 1L;
        foreach (var evt in events)
        {
            evt.SequenceNumber = sequence++;
            sequenced.Add(evt);
        }

        var now = DateTimeOffset.UtcNow;
        var document = new ThreadEventDocument
        {
            SessionId = sessionId,
            ThreadId = threadId,
            CreatedAt = createdAt ?? sequenced.FirstOrDefault()?.Timestamp ?? now,
            UpdatedAt = updatedAt ?? sequenced.LastOrDefault()?.Timestamp ?? now,
            NextSequenceNumber = sequence,
            Events = sequenced
        };

        ThreadEventValidation.RequireDocumentScope(document, sessionId, threadId);
        return document;
    }
}
