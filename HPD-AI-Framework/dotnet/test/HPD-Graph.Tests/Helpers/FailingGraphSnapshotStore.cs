using HPDAgent.Graph.Abstractions.Caching;

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Snapshot store implementation that throws exceptions for testing error handling.
/// </summary>
public class FailingGraphSnapshotStore : IGraphSnapshotStore
{
    private readonly bool _throwOnLoad;
    private readonly bool _throwOnSave;
    private readonly bool _throwOnList;
    private readonly bool _throwOnPrune;

    public FailingGraphSnapshotStore(
        bool throwOnLoad = false,
        bool throwOnSave = false,
        bool throwOnList = false,
        bool throwOnPrune = false)
    {
        _throwOnLoad = throwOnLoad;
        _throwOnSave = throwOnSave;
        _throwOnList = throwOnList;
        _throwOnPrune = throwOnPrune;
    }

    public Task<GraphSnapshot?> GetLatestSnapshotAsync(string graphId, CancellationToken ct = default)
    {
        if (_throwOnLoad)
        {
            throw new InvalidOperationException("Simulated snapshot load failure");
        }

        return Task.FromResult<GraphSnapshot?>(null);
    }

    public Task SaveSnapshotAsync(string graphId, GraphSnapshot snapshot, CancellationToken ct = default)
    {
        if (_throwOnSave)
        {
            throw new InvalidOperationException("Simulated snapshot save failure");
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<GraphSnapshot> ListSnapshotsAsync(string graphId, CancellationToken ct = default)
    {
        if (_throwOnList)
        {
            throw new InvalidOperationException("Simulated snapshot list failure");
        }

        yield break;
        await Task.CompletedTask;
    }

    public Task PruneOldSnapshotsAsync(string graphId, int keepLastN = 10, CancellationToken ct = default)
    {
        if (_throwOnPrune)
        {
            throw new InvalidOperationException("Simulated snapshot prune failure");
        }

        return Task.CompletedTask;
    }
}
