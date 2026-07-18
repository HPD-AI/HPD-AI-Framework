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

        var ownerAgentId = evt switch
        {
            ThreadCreatedEvent created => created.OwnerAgentId,
            ThreadUpdatedEvent updated => updated.OwnerAgentId,
            _ => null
        };
        if (ownerAgentId is not null && string.IsNullOrWhiteSpace(ownerAgentId))
            throw new InvalidOperationException("Thread create/update events require a stable owner AgentId.");
    }

}
