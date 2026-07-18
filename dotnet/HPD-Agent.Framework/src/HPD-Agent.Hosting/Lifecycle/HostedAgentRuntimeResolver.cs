namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Resolves durable thread runtimes through the hosting runtime cache.
/// </summary>
public sealed class HostedAgentRuntimeResolver(AgentManager agents) : IAgentRuntimeResolver
{
    /// <inheritdoc />
    public async Task<IAgentRuntimeLease> GetOrBuildAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetOrBuildAgentRuntimeAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        return new HostedLease(agent);
    }

    private sealed class HostedLease(Agent agent) : IAgentRuntimeLease
    {
        public Agent Agent { get; } = agent;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
