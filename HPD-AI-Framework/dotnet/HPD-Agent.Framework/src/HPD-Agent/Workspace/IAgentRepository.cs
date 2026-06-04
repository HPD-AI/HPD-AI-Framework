namespace HPD.Agent;

/// <summary>
/// Typed facade for stored agent definitions backed by workspace agent spaces.
/// </summary>
public interface IAgentRepository
{
    Task<StoredAgent?> LoadAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        StoredAgent agent,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListIdsAsync(
        CancellationToken cancellationToken = default);
}
