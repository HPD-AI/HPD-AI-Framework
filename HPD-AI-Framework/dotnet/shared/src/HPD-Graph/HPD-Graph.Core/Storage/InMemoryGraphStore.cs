using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Checkpointing;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// Combined in-memory graph store for definitions and checkpoints.
/// </summary>
public sealed class InMemoryGraphStore : IGraphStore
{
    private readonly InMemoryGraphDefinitionStore _definitions;
    private readonly InMemoryCheckpointStore _checkpoints;

    public InMemoryGraphStore()
        : this(new InMemoryGraphDefinitionStore(), new InMemoryCheckpointStore())
    {
    }

    public InMemoryGraphStore(CheckpointRetentionMode retentionMode)
        : this(new InMemoryGraphDefinitionStore(), new InMemoryCheckpointStore { RetentionMode = retentionMode })
    {
    }

    public InMemoryGraphStore(
        InMemoryGraphDefinitionStore definitions,
        InMemoryCheckpointStore checkpoints)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
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

    public void Clear()
    {
        _definitions.Clear();
        _checkpoints.Clear();
    }
}
