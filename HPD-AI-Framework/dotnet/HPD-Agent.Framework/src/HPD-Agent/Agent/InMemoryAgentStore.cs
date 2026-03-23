using System.Collections.Concurrent;

namespace HPD.Agent;

/// <summary>
/// In-memory <see cref="IAgentStore"/> for development and testing.
/// Not persistent — all definitions are lost on process restart.
/// </summary>
public class InMemoryAgentStore : IAgentStore
{
    private readonly ConcurrentDictionary<string, StoredAgent> _agents = new();

    public Task<StoredAgent?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        _agents.TryGetValue(agentId, out var agent);
        return Task.FromResult(agent);
    }

    public Task SaveAsync(StoredAgent agent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.Id);
        _agents[agent.Id] = agent;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        _agents.TryRemove(agentId, out _);
        return Task.CompletedTask;
    }

    public Task<List<string>> ListIdsAsync(CancellationToken ct = default)
        => Task.FromResult(_agents.Keys.ToList());
}
