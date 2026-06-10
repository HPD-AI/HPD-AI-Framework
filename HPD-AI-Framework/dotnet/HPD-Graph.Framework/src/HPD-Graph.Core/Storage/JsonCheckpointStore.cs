using System.Text.Json;
using HPD.Graph.Abstractions.Checkpointing;
using HPD.Graph.Abstractions.Serialization;

namespace HPD.Graph.Core.Storage;

/// <summary>
/// File-backed checkpoint store that preserves latest-only and full-history retention.
/// </summary>
public sealed class JsonCheckpointStore : IGraphCheckpointStore
{
    private readonly string _checkpointsDirectory;

    public JsonCheckpointStore(string rootDirectory, CheckpointRetentionMode retentionMode = CheckpointRetentionMode.LatestOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _checkpointsDirectory = Path.Combine(rootDirectory, "checkpoints");
        RetentionMode = retentionMode;
    }

    public CheckpointRetentionMode RetentionMode { get; }

    public async Task SaveCheckpointAsync(GraphCheckpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.CheckpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.ExecutionId);
        ct.ThrowIfCancellationRequested();

        var executionDirectory = GetExecutionDirectory(checkpoint.ExecutionId);
        Directory.CreateDirectory(executionDirectory);

        var path = GetCheckpointPath(checkpoint);
        await WriteJsonAsync(path, checkpoint, ct);

        if (RetentionMode == CheckpointRetentionMode.LatestOnly)
        {
            foreach (var oldPath in Directory.EnumerateFiles(executionDirectory, "*.checkpoint.json").Where(p => !StringComparer.Ordinal.Equals(p, path)))
            {
                File.Delete(oldPath);
            }
        }
    }

    public async Task<GraphCheckpoint?> LoadLatestCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        var checkpoints = await ListCheckpointsAsync(executionId, ct);
        return checkpoints.Count == 0 ? null : checkpoints[^1];
    }

    public async Task<GraphCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(_checkpointsDirectory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(_checkpointsDirectory, "*.checkpoint.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var checkpoint = await ReadCheckpointAsync(path, ct);
            if (checkpoint is not null && StringComparer.Ordinal.Equals(checkpoint.CheckpointId, checkpointId))
            {
                return checkpoint;
            }
        }

        return null;
    }

    public Task DeleteCheckpointsAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        var executionDirectory = GetExecutionDirectory(executionId);
        if (Directory.Exists(executionDirectory))
        {
            Directory.Delete(executionDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<GraphCheckpoint>> ListCheckpointsAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        var executionDirectory = GetExecutionDirectory(executionId);
        if (!Directory.Exists(executionDirectory))
        {
            return Array.Empty<GraphCheckpoint>();
        }

        var checkpoints = new List<GraphCheckpoint>();
        foreach (var path in Directory.EnumerateFiles(executionDirectory, "*.checkpoint.json").OrderBy(static p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var checkpoint = await ReadCheckpointAsync(path, ct);
            if (checkpoint is not null)
            {
                checkpoints.Add(checkpoint);
            }
        }

        return checkpoints.OrderBy(static checkpoint => checkpoint.CreatedAt).ToList();
    }

    private string GetExecutionDirectory(string executionId) =>
        Path.Combine(_checkpointsDirectory, EncodeFileName(executionId));

    private string GetCheckpointPath(GraphCheckpoint checkpoint)
    {
        var timestamp = checkpoint.CreatedAt.UtcTicks.ToString("D20");
        return Path.Combine(
            GetExecutionDirectory(checkpoint.ExecutionId),
            $"{timestamp}-{EncodeFileName(checkpoint.CheckpointId)}.checkpoint.json");
    }

    private static async Task WriteJsonAsync(string path, GraphCheckpoint checkpoint, CancellationToken ct)
    {
        var dto = ToJsonCheckpoint(checkpoint);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                dto,
                StorageJsonSerializerContext.Default.JsonGraphCheckpoint,
                ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task<GraphCheckpoint?> ReadCheckpointAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var dto = await JsonSerializer.DeserializeAsync(
            stream,
            StorageJsonSerializerContext.Default.JsonGraphCheckpoint,
            ct);

        return dto is null ? null : FromJsonCheckpoint(dto);
    }

    private static JsonGraphCheckpoint ToJsonCheckpoint(GraphCheckpoint checkpoint) => new()
    {
        CheckpointId = checkpoint.CheckpointId,
        ExecutionId = checkpoint.ExecutionId,
        GraphId = checkpoint.GraphId,
        CreatedAt = checkpoint.CreatedAt,
        CompletedNodes = checkpoint.CompletedNodes.ToHashSet(StringComparer.Ordinal),
        NodeOutputs = ToJsonElementDictionary(checkpoint.NodeOutputs),
        ContextJson = checkpoint.ContextJson,
        NodeStateMetadata = checkpoint.NodeStateMetadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal),
        Metadata = checkpoint.Metadata is null ? null : ToJsonMetadata(checkpoint.Metadata),
        SchemaVersion = checkpoint.SchemaVersion,
        CurrentIteration = checkpoint.CurrentIteration,
        PendingDirtyNodes = checkpoint.PendingDirtyNodes.ToHashSet(StringComparer.Ordinal)
    };

    private static GraphCheckpoint FromJsonCheckpoint(JsonGraphCheckpoint checkpoint) => new()
    {
        CheckpointId = checkpoint.CheckpointId,
        ExecutionId = checkpoint.ExecutionId,
        GraphId = checkpoint.GraphId,
        CreatedAt = checkpoint.CreatedAt,
        CompletedNodes = checkpoint.CompletedNodes,
        NodeOutputs = checkpoint.NodeOutputs.ToDictionary(
            pair => pair.Key,
            pair => (object)pair.Value.Clone(),
            StringComparer.Ordinal),
        ContextJson = checkpoint.ContextJson,
        NodeStateMetadata = checkpoint.NodeStateMetadata,
        Metadata = checkpoint.Metadata is null ? null : FromJsonMetadata(checkpoint.Metadata),
        SchemaVersion = checkpoint.SchemaVersion,
        CurrentIteration = checkpoint.CurrentIteration,
        PendingDirtyNodes = checkpoint.PendingDirtyNodes
    };

    private static JsonCheckpointMetadata ToJsonMetadata(CheckpointMetadata metadata) => new()
    {
        Trigger = metadata.Trigger,
        CompletedNodeId = metadata.CompletedNodeId,
        CompletedLayer = metadata.CompletedLayer,
        CustomMetadata = metadata.CustomMetadata is null ? null : ToJsonElementDictionary(metadata.CustomMetadata),
        IterationIndex = metadata.IterationIndex,
        SuspendedNodeId = metadata.SuspendedNodeId,
        SuspendToken = metadata.SuspendToken,
        SuspensionOutcome = metadata.SuspensionOutcome
    };

    private static CheckpointMetadata FromJsonMetadata(JsonCheckpointMetadata metadata) => new()
    {
        Trigger = metadata.Trigger,
        CompletedNodeId = metadata.CompletedNodeId,
        CompletedLayer = metadata.CompletedLayer,
        CustomMetadata = metadata.CustomMetadata?.ToDictionary(
            pair => pair.Key,
            pair => (object)pair.Value.Clone(),
            StringComparer.Ordinal),
        IterationIndex = metadata.IterationIndex,
        SuspendedNodeId = metadata.SuspendedNodeId,
        SuspendToken = metadata.SuspendToken,
        SuspensionOutcome = metadata.SuspensionOutcome
    };

    private static Dictionary<string, JsonElement> ToJsonElementDictionary(IReadOnlyDictionary<string, object> values) =>
        values.ToDictionary(
            pair => pair.Key,
            pair => ToJsonElement(pair.Value),
            StringComparer.Ordinal);

    private static JsonElement ToJsonElement(object value) =>
        value is JsonElement element
            ? element.Clone()
            : GraphJsonValue.ToJsonElement(value, "checkpoint value");

    private static string EncodeFileName(string value) => Uri.EscapeDataString(value);
}
