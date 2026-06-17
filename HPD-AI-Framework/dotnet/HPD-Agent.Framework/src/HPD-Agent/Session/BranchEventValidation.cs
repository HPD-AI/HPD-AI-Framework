namespace HPD.Agent;

internal static class BranchEventValidation
{
    public static AgentEvent PrepareForAppend(string sessionId, string branchId, AgentEvent evt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(evt);

        evt = evt with
        {
            EventId = string.IsNullOrWhiteSpace(evt.EventId)
                ? Guid.NewGuid().ToString("N")
                : evt.EventId,
            SessionId = string.IsNullOrWhiteSpace(evt.SessionId)
                ? sessionId
                : evt.SessionId,
            BranchId = string.IsNullOrWhiteSpace(evt.BranchId)
                ? branchId
                : evt.BranchId
        };

        RequirePersistableScope(sessionId, branchId, evt);
        return evt;
    }

    public static void RequirePersistableScope(string sessionId, string branchId, AgentEvent evt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(evt);

        if (string.IsNullOrWhiteSpace(evt.EventId))
            throw new InvalidOperationException("Branch event must have an EventId before it is appended.");

        if (!StringComparer.Ordinal.Equals(evt.SessionId, sessionId))
        {
            throw new InvalidOperationException(
                $"Branch event session scope '{evt.SessionId ?? "<null>"}' does not match target session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(evt.BranchId, branchId))
        {
            throw new InvalidOperationException(
                $"Branch event branch scope '{evt.BranchId ?? "<null>"}' does not match target branch '{branchId}'.");
        }
    }

    public static BranchEventDocument HydrateDocumentEventScope(BranchEventDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var events = document.Events
            .Select(evt => evt with
            {
                SessionId = string.IsNullOrWhiteSpace(evt.SessionId)
                    ? document.SessionId
                    : evt.SessionId,
                BranchId = string.IsNullOrWhiteSpace(evt.BranchId)
                    ? document.BranchId
                    : evt.BranchId
            })
            .ToList();

        return document with { Events = events };
    }

    public static AgentEvent HydrateEventScope(string sessionId, string branchId, AgentEvent evt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(evt);

        return evt with
        {
            SessionId = string.IsNullOrWhiteSpace(evt.SessionId)
                ? sessionId
                : evt.SessionId,
            BranchId = string.IsNullOrWhiteSpace(evt.BranchId)
                ? branchId
                : evt.BranchId
        };
    }

    public static void RequireDocumentScope(BranchEventDocument document, string sessionId, string branchId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!StringComparer.Ordinal.Equals(document.SessionId, sessionId))
        {
            throw new InvalidDataException(
                $"Branch document session scope '{document.SessionId}' does not match requested session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(document.BranchId, branchId))
        {
            throw new InvalidDataException(
                $"Branch document branch scope '{document.BranchId}' does not match requested branch '{branchId}'.");
        }

        foreach (var evt in document.Events)
            RequirePersistableScope(sessionId, branchId, evt);
    }
}
