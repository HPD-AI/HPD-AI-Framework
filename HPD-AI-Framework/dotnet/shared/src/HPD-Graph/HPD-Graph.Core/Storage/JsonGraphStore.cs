using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// Combined file-backed graph store for definitions and checkpoints.
/// </summary>
public sealed class JsonGraphStore : IGraphStore
{
    private readonly JsonGraphDefinitionStore _definitions;
    private readonly JsonCheckpointStore _checkpoints;

    public JsonGraphStore(string rootDirectory, CheckpointRetentionMode retentionMode = CheckpointRetentionMode.LatestOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _definitions = new JsonGraphDefinitionStore(rootDirectory);
        _checkpoints = new JsonCheckpointStore(rootDirectory, retentionMode);
    }

    public CheckpointRetentionMode RetentionMode => _checkpoints.RetentionMode;

    public Task<StoredGraph?> LoadAsync(string graphId, CancellationToken ct = default) =>
        _definitions.LoadAsync(graphId, ct);

    public Task SaveAsync(StoredGraph graph, CancellationToken ct = default) =>
        _definitions.SaveAsync(graph, ct);

    public Task DeleteAsync(string graphId, CancellationToken ct = default) =>
        _definitions.DeleteAsync(graphId, ct);

    public Task<IReadOnlyList<StoredGraphSummary>> ListAsync(CancellationToken ct = default) =>
        _definitions.ListAsync(ct);

    public Task SaveCheckpointAsync(GraphCheckpoint checkpoint, CancellationToken ct = default) =>
        _checkpoints.SaveCheckpointAsync(checkpoint, ct);

    public Task<GraphCheckpoint?> LoadLatestCheckpointAsync(string executionId, CancellationToken ct = default) =>
        _checkpoints.LoadLatestCheckpointAsync(executionId, ct);

    public Task<GraphCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken ct = default) =>
        _checkpoints.LoadCheckpointAsync(checkpointId, ct);

    public Task DeleteCheckpointsAsync(string executionId, CancellationToken ct = default) =>
        _checkpoints.DeleteCheckpointsAsync(executionId, ct);

    public Task<IReadOnlyList<GraphCheckpoint>> ListCheckpointsAsync(string executionId, CancellationToken ct = default) =>
        _checkpoints.ListCheckpointsAsync(executionId, ct);
}
