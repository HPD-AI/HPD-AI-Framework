using System.Text.Json;
using System.Text.Json.Serialization;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Abstractions.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
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
[JsonSerializable(typeof(ScheduledGraph))]
[JsonSerializable(typeof(StoredGraph))]
[JsonSerializable(typeof(StoredGraphSummary))]
[JsonSerializable(typeof(WorkflowExecution))]
[JsonSerializable(typeof(WorkflowSuspension))]
[JsonSerializable(typeof(WorkflowLogEntry))]
[JsonSerializable(typeof(HandlerDescriptor))]
[JsonSerializable(typeof(SocketDescriptor))]
[JsonSerializable(typeof(ConfigDescriptor))]
[JsonSerializable(typeof(Dictionary<string, NodeConfig>))]
[JsonSerializable(typeof(Dictionary<string, HandlerDescriptor>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<EdgeConfig>))]
[JsonSerializable(typeof(List<ScheduledGraph>))]
[JsonSerializable(typeof(List<StoredGraphSummary>))]
[JsonSerializable(typeof(List<WorkflowExecution>))]
[JsonSerializable(typeof(List<WorkflowSuspension>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
public partial class GraphConfigJsonSerializerContext : JsonSerializerContext
{
}
