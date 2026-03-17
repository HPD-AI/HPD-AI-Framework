using System.Text.Json;
using System.Text.Json.Serialization;
using HPDAgent.Graph.Abstractions.Checkpointing;

namespace HPDAgent.Graph.Abstractions.Serialization;

/// <summary>
/// Unified source-generated JSON context for ALL HPD-Graph serialization.
/// Native AOT compatible.
///
/// Covers:
/// - Node outputs (Dictionary&lt;string, object&gt;)
/// - Checkpointing (GraphCheckpoint, NodeStateMetadata)
/// - Polling state (PollingState)
/// - Event metadata
/// - Context serialization
/// </summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
)]
// === Node Outputs ===
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]

// === Primitive Types (80-90% of outputs) ===
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]

// === Nullable Primitives ===
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(DateTimeOffset?))]
[JsonSerializable(typeof(Guid?))]

// === Collections ===
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(List<long>))]
[JsonSerializable(typeof(List<double>))]
[JsonSerializable(typeof(List<bool>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(object[]))]

// === Nested Dictionaries ===
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(Dictionary<string, bool>))]

// === Checkpointing Types ===
[JsonSerializable(typeof(GraphCheckpoint))]
[JsonSerializable(typeof(NodeStateMetadata))]
[JsonSerializable(typeof(CheckpointMetadata))]
[JsonSerializable(typeof(List<NodeStateMetadata>))]
[JsonSerializable(typeof(List<string>))]  // CompletedNodes, etc.

// === Polling State (V5 Primitives) ===
[JsonSerializable(typeof(PollingState))]

// === Context Serialization ===
[JsonSerializable(typeof(ContextMetadata))]  // For checkpoint ContextJson

// === Partition Definitions (Polymorphic) ===
[JsonSerializable(typeof(HPDAgent.Graph.Abstractions.Artifacts.PartitionDefinition))]
[JsonSerializable(typeof(HPDAgent.Graph.Abstractions.Artifacts.StaticPartitionDefinition))]
[JsonSerializable(typeof(HPDAgent.Graph.Abstractions.Artifacts.TimePartitionDefinition))]
[JsonSerializable(typeof(HPDAgent.Graph.Abstractions.Artifacts.MultiPartitionDefinition))]
[JsonSerializable(typeof(HPDAgent.Graph.Abstractions.Artifacts.PartitionSnapshot))]
[JsonSerializable(typeof(HPDAgent.Graph.Abstractions.Artifacts.PartitionKey))]
[JsonSerializable(typeof(List<HPDAgent.Graph.Abstractions.Artifacts.PartitionKey>))]

// === JsonElement for Polymorphic Support ===
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(object))]

public partial class GraphJsonSerializerContext : JsonSerializerContext
{
}
