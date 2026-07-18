namespace HPD.Agent;

/// <summary>
/// Resolves a thread-owned runtime for a durable agent definition.
/// </summary>
public interface IAgentRuntimeResolver
{
    /// <summary>Acquires the runtime identified by agent, session, and thread.</summary>
    Task<IAgentRuntimeLease> GetOrBuildAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Controls the lifetime of an acquired thread runtime.
/// </summary>
public interface IAgentRuntimeLease : IAsyncDisposable
{
    /// <summary>The acquired thread-owned agent runtime.</summary>
    Agent Agent { get; }
}
