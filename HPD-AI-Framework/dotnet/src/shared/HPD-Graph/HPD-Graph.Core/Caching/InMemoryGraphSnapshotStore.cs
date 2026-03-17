using HPDAgent.Graph.Abstractions.Caching;

namespace HPDAgent.Graph.Core.Caching;

/// <summary>
/// In-memory implementation of IGraphSnapshotStore.
/// Thread-safe for single-process deployments.
/// For distributed deployments, use a persistent store (PostgreSQL, MySQL, etc.).
/// </summary>
public class InMemoryGraphSnapshotStore : IGraphSnapshotStore
{
    private readonly Dictionary<string, List<GraphSnapshot>> _snapshots = new();
    private readonly object _lock = new();

    public Task<GraphSnapshot?> GetLatestSnapshotAsync(string graphId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_snapshots.TryGetValue(graphId, out var snapshots) || snapshots.Count == 0)
            {
                return Task.FromResult<GraphSnapshot?>(null);
            }

            // Return most recent snapshot
            return Task.FromResult<GraphSnapshot?>(snapshots.OrderByDescending(s => s.Timestamp).First());
        }
    }

    public Task SaveSnapshotAsync(string graphId, GraphSnapshot snapshot, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_snapshots.ContainsKey(graphId))
            {
                _snapshots[graphId] = new List<GraphSnapshot>();
            }

            _snapshots[graphId].Add(snapshot);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<GraphSnapshot> ListSnapshotsAsync(string graphId, CancellationToken ct = default)
    {
        List<GraphSnapshot> snapshots;
        lock (_lock)
        {
            if (!_snapshots.TryGetValue(graphId, out var snapshotList))
            {
                yield break;
            }

            snapshots = snapshotList.OrderByDescending(s => s.Timestamp).ToList();
        }

        foreach (var snapshot in snapshots)
        {
            yield return snapshot;
        }

        await Task.CompletedTask;  // Satisfy async requirement
    }

    public Task PruneOldSnapshotsAsync(string graphId, int keepLastN = 10, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_snapshots.TryGetValue(graphId, out var snapshotList))
            {
                return Task.CompletedTask;
            }

            // Keep only last N snapshots
            var toKeep = snapshotList
                .OrderByDescending(s => s.Timestamp)
                .Take(keepLastN)
                .ToList();

            _snapshots[graphId] = toKeep;
        }

        return Task.CompletedTask;
    }
}
