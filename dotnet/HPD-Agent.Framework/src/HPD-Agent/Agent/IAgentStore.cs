namespace HPD.Agent;

/// <summary>
/// Persistence interface for <see cref="StoredAgent"/> definitions.
/// Mirrors <see cref="ISessionStore"/> in purpose and design.
/// </summary>
public interface IAgentStore
{
    /// <summary>Gets whether stored agent definitions survive process restart.</summary>
    StorePersistenceCapability PersistenceCapability =>
        StorePersistenceCapability.Ephemeral;

    Task<StoredAgent?> LoadAsync(string agentId, CancellationToken ct = default);
    Task SaveAsync(StoredAgent agent, CancellationToken ct = default);
    Task DeleteAsync(string agentId, CancellationToken ct = default);
    Task<List<string>> ListIdsAsync(CancellationToken ct = default);
}
