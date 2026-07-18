namespace HPD.Agent;

/// <summary>
/// Resolves a runtime for a durable agent definition and thread scope.
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
    /// <summary>The acquired agent runtime for the requested thread scope.</summary>
    Agent Agent { get; }
}
