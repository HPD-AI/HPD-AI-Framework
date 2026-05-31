namespace HPD.Agent;

/// <summary>
/// Thrown when a branch fork is requested from a message id that is no longer present.
/// </summary>
public sealed class MessageNotPresentOnBranchException : InvalidOperationException
{
    public MessageNotPresentOnBranchException(
        string sessionId,
        string branchId,
        string messageId,
        IReadOnlyList<string>? replacementMessageIds = null)
        : base(BuildMessage(sessionId, branchId, messageId, replacementMessageIds))
    {
        SessionId = sessionId;
        BranchId = branchId;
        MessageId = messageId;
        ReplacementMessageIds = replacementMessageIds ?? [];
    }

    public string SessionId { get; }

    public string BranchId { get; }

    public string MessageId { get; }

    public IReadOnlyList<string> ReplacementMessageIds { get; }

    private static string BuildMessage(
        string sessionId,
        string branchId,
        string messageId,
        IReadOnlyList<string>? replacementMessageIds)
    {
        var message = $"Cannot fork branch '{branchId}' in session '{sessionId}' from message '{messageId}' because that message is no longer present.";
        return replacementMessageIds is { Count: > 0 }
            ? $"{message} Replacement message candidates: {string.Join(", ", replacementMessageIds)}."
            : message;
    }
}
