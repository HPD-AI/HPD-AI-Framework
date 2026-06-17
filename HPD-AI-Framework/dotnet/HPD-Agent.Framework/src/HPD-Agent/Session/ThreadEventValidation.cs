namespace HPD.Agent;

internal static class ThreadEventValidation
{
    public static AgentEvent PrepareForAppend(string sessionId, string threadId, AgentEvent evt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(evt);

        evt = evt with
        {
            EventId = string.IsNullOrWhiteSpace(evt.EventId)
                ? Guid.NewGuid().ToString("N")
                : evt.EventId,
            SessionId = string.IsNullOrWhiteSpace(evt.SessionId)
                ? sessionId
                : evt.SessionId,
            ThreadId = string.IsNullOrWhiteSpace(evt.ThreadId)
                ? threadId
                : evt.ThreadId
        };

        RequirePersistableScope(sessionId, threadId, evt);
        return evt;
    }

    public static void RequirePersistableScope(string sessionId, string threadId, AgentEvent evt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(evt);

        if (string.IsNullOrWhiteSpace(evt.EventId))
            throw new InvalidOperationException("Thread event must have an EventId before it is appended.");

        if (!StringComparer.Ordinal.Equals(evt.SessionId, sessionId))
        {
            throw new InvalidOperationException(
                $"Thread event session scope '{evt.SessionId ?? "<null>"}' does not match target session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(evt.ThreadId, threadId))
        {
            throw new InvalidOperationException(
                $"Thread event thread scope '{evt.ThreadId ?? "<null>"}' does not match target thread '{threadId}'.");
        }
    }

    public static ThreadEventDocument HydrateDocumentEventScope(ThreadEventDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var events = document.Events
            .Select(evt => evt with
            {
                SessionId = string.IsNullOrWhiteSpace(evt.SessionId)
                    ? document.SessionId
                    : evt.SessionId,
                ThreadId = string.IsNullOrWhiteSpace(evt.ThreadId)
                    ? document.ThreadId
                    : evt.ThreadId
            })
            .ToList();

        return document with { Events = events };
    }

    public static AgentEvent HydrateEventScope(string sessionId, string threadId, AgentEvent evt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(evt);

        return evt with
        {
            SessionId = string.IsNullOrWhiteSpace(evt.SessionId)
                ? sessionId
                : evt.SessionId,
            ThreadId = string.IsNullOrWhiteSpace(evt.ThreadId)
                ? threadId
                : evt.ThreadId
        };
    }

    public static void RequireDocumentScope(ThreadEventDocument document, string sessionId, string threadId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!StringComparer.Ordinal.Equals(document.SessionId, sessionId))
        {
            throw new InvalidDataException(
                $"Thread document session scope '{document.SessionId}' does not match requested session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(document.ThreadId, threadId))
        {
            throw new InvalidDataException(
                $"Thread document thread scope '{document.ThreadId}' does not match requested thread '{threadId}'.");
        }

        foreach (var evt in document.Events)
            RequirePersistableScope(sessionId, threadId, evt);
    }
}
