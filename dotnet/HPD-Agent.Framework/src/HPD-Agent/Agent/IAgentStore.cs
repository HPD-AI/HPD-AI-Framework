namespace HPD.Agent;

/// <summary>
/// Persistence interface for <see cref="StoredAgent"/> definitions.
/// Mirrors <see cref="ISessionStore"/> in purpose and design.
/// </summary>
public interface IAgentStore
{
    Task<StoredAgent?> LoadAsync(string agentId, CancellationToken ct = default);
    Task SaveAsync(StoredAgent agent, CancellationToken ct = default);
    Task DeleteAsync(string agentId, CancellationToken ct = default);
    Task<List<string>> ListIdsAsync(CancellationToken ct = default);
}
