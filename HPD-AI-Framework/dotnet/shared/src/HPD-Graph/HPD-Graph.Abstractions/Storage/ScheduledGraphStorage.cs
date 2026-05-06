using HPDAgent.Graph.Abstractions.Config;

namespace HPDAgent.Graph.Abstractions.Storage;

public interface IScheduledGraphStore
{
    Task<ScheduledGraph?> LoadAsync(string graphId, CancellationToken ct = default);
    Task SaveAsync(ScheduledGraph schedule, CancellationToken ct = default);
    Task DeleteAsync(string graphId, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledGraph>> ListAsync(CancellationToken ct = default);
}

