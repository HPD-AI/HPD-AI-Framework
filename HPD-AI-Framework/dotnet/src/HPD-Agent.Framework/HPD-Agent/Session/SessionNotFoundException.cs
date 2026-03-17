namespace HPD.Agent;

/// <summary>
/// Thrown when a session or branch cannot be found in the configured store.
/// This indicates the session was never created, has been deleted, or the store
/// has been cleared since the session was created.
/// </summary>
/// <remarks>
/// Use <c>agent.CreateSessionAsync(sessionId)</c> to explicitly create a session
/// before calling <c>RunAsync</c>.
/// </remarks>
public class SessionNotFoundException : InvalidOperationException
{
    /// <summary>The session ID that could not be found.</summary>
    public string SessionId { get; }

    /// <summary>The branch ID that could not be found. Null if the session itself was missing.</summary>
    public string? BranchId { get; }

    public SessionNotFoundException(string sessionId)
        : base($"Session '{sessionId}' was not found in the store. " +
               $"Call agent.CreateSessionAsync(\"{sessionId}\") before running.")
    {
        SessionId = sessionId;
    }

    public SessionNotFoundException(string sessionId, string branchId)
        : base($"Branch '{branchId}' in session '{sessionId}' was not found in the store.")
    {
        SessionId = sessionId;
        BranchId = branchId;
    }
}
