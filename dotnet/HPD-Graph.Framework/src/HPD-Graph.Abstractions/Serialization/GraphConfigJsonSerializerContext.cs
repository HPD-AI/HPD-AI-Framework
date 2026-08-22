using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Discovery;
using HPD.Graph.Abstractions.Storage;

namespace HPD.Graph.Abstractions.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(GraphConfig))]
[JsonSerializable(typeof(NodeConfig))]
[JsonSerializable(typeof(EdgeConfig))]
[JsonSerializable(typeof(ConditionConfig))]
[JsonSerializable(typeof(RetryPolicyConfig))]
[JsonSerializable(typeof(EdgeRetryPolicyConfig))]
[JsonSerializable(typeof(ScheduleConstraintConfig))]
[JsonSerializable(typeof(SuspensionOptionsConfig))]
[JsonSerializable(typeof(ErrorPropagationPolicyConfig))]
[JsonSerializable(typeof(IterationOptionsConfig))]
[JsonSerializable(typeof(ArtifactDependencyConfig))]
[JsonSerializable(typeof(PartitionDefinitionConfig))]
[JsonSerializable(typeof(PartitionDependencyConfig))]
[JsonSerializable(typeof(PartitionDependencyMappingKindConfig))]
[JsonSerializable(typeof(CustomPrimitiveDescriptorConfig))]
[JsonSerializable(typeof(StaticPartitionDefinition))]
[JsonSerializable(typeof(TimePartitionDefinition))]
[JsonSerializable(typeof(MultiPartitionDefinition))]
[JsonSerializable(typeof(List<PartitionDefinition>))]
[JsonSerializable(typeof(CacheOptionsConfig))]
[JsonSerializable(typeof(InputSchemaConfig))]
[JsonSerializable(typeof(MapErrorModeConfig))]
[JsonSerializable(typeof(GraphScheduleConfig))]
[JsonSerializable(typeof(StoredGraph))]
[JsonSerializable(typeof(StoredGraphSummary))]
[JsonSerializable(typeof(WorkflowLogEntry))]
[JsonSerializable(typeof(HandlerDescriptor))]
[JsonSerializable(typeof(SocketDescriptor))]
[JsonSerializable(typeof(ConfigDescriptor))]
[JsonSerializable(typeof(Dictionary<string, NodeConfig>))]
[JsonSerializable(typeof(Dictionary<string, HandlerDescriptor>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<EdgeConfig>))]
[JsonSerializable(typeof(List<StoredGraphSummary>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
public partial class GraphConfigJsonSerializerContext : JsonSerializerContext
{
}
