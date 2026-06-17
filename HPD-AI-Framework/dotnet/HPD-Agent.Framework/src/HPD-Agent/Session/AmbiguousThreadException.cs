namespace HPD.Agent;

/// <summary>
/// Thrown when threadId is not specified but the session has multiple threads.
/// Once a session has been forked (via ForkThreadAsync), the caller must specify
/// threadId explicitly to avoid accidentally writing to the wrong thread.
/// </summary>
public class AmbiguousThreadException : InvalidOperationException
{
    /// <summary>The session that has multiple threads.</summary>
    public string SessionId { get; }

    /// <summary>The thread IDs available in the session.</summary>
    public List<string> AvailableThreads { get; }

    public AmbiguousThreadException(string sessionId, List<string> threads)
        : base($"Session '{sessionId}' has {threads.Count} threads ({string.Join(", ", threads)}). " +
               $"Specify threadId explicitly when a session has multiple threads.")
    {
        SessionId = sessionId;
        AvailableThreads = threads;
    }
}
