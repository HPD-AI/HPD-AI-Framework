namespace HPD.Agent;

/// <summary>
/// Thrown when a thread fork is requested from a message id that is no longer present.
/// </summary>
public sealed class MessageNotPresentOnThreadException : InvalidOperationException
{
    public MessageNotPresentOnThreadException(
        string sessionId,
        string threadId,
        string messageId,
        IReadOnlyList<string>? replacementMessageIds = null)
        : base(BuildMessage(sessionId, threadId, messageId, replacementMessageIds))
    {
        SessionId = sessionId;
        ThreadId = threadId;
        MessageId = messageId;
        ReplacementMessageIds = replacementMessageIds ?? [];
    }

    public string SessionId { get; }

    public string ThreadId { get; }

    public string MessageId { get; }

    public IReadOnlyList<string> ReplacementMessageIds { get; }

    private static string BuildMessage(
        string sessionId,
        string threadId,
        string messageId,
        IReadOnlyList<string>? replacementMessageIds)
    {
        var message = $"Cannot fork thread '{threadId}' in session '{sessionId}' from message '{messageId}' because that message is no longer present.";
        return replacementMessageIds is { Count: > 0 }
            ? $"{message} Replacement message candidates: {string.Join(", ", replacementMessageIds)}."
            : message;
    }
}
