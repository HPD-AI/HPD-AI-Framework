namespace HPD.Agent;

public static class ThreadEventDocumentBuilder
{
    public static ThreadEventDocument FromInitialThread(string sessionId, Thread thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(thread);

        var createdAt = new DateTimeOffset(thread.CreatedAt, TimeSpan.Zero);
        var events = new List<AgentEvent>();
        events.Add(ThreadEventFactory.ThreadCreated(thread));

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
