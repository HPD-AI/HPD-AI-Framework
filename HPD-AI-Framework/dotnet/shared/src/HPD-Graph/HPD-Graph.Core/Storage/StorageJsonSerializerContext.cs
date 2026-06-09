using System.Text.Json;
using System.Text.Json.Serialization;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Execution;

namespace HPDAgent.Graph.Core.Storage;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(JsonGraphCheckpoint))]
[JsonSerializable(typeof(JsonCheckpointMetadata))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, NodeStateMetadata>))]
[JsonSerializable(typeof(HashSet<string>))]
internal partial class StorageJsonSerializerContext : JsonSerializerContext
{
}

internal sealed record JsonGraphCheckpoint
{
    public required string CheckpointId { get; init; }
    public required string ExecutionId { get; init; }
    public required string GraphId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required HashSet<string> CompletedNodes { get; init; }
    public required Dictionary<string, JsonElement> NodeOutputs { get; init; }
    public required string ContextJson { get; init; }
    public Dictionary<string, NodeStateMetadata> NodeStateMetadata { get; init; } = new(StringComparer.Ordinal);
    public JsonCheckpointMetadata? Metadata { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public int CurrentIteration { get; init; }
    public HashSet<string> PendingDirtyNodes { get; init; } = [];
}

internal sealed record JsonCheckpointMetadata
{
    public required CheckpointTrigger Trigger { get; init; }
    public string? CompletedNodeId { get; init; }
    public int? CompletedLayer { get; init; }
    public Dictionary<string, JsonElement>? CustomMetadata { get; init; }
    public int? IterationIndex { get; init; }
    public string? SuspendedNodeId { get; init; }
    public string? SuspendToken { get; init; }
    public SuspensionOutcome? SuspensionOutcome { get; init; }
}
