using System.Text.Json.Serialization;
using HPDAgent.Graph.Hosting.Data;

namespace HPDAgent.Graph.Hosting.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CreateWorkflowRequest))]
[JsonSerializable(typeof(UpdateWorkflowRequest))]
[JsonSerializable(typeof(ExecuteWorkflowRequest))]
[JsonSerializable(typeof(WorkflowDto))]
[JsonSerializable(typeof(WorkflowListResponse))]
[JsonSerializable(typeof(WorkflowStatusDto))]
[JsonSerializable(typeof(WorkflowExecutionDto))]
[JsonSerializable(typeof(GraphLogEntryDto))]
[JsonSerializable(typeof(ResumeSuspensionRequest))]
[JsonSerializable(typeof(ResumeSuspensionResultDto))]
[JsonSerializable(typeof(SuspendedNodeDto))]
[JsonSerializable(typeof(PollingStatusDto))]
[JsonSerializable(typeof(HandlerCatalogResponse))]
[JsonSerializable(typeof(CreateScheduleRequest))]
[JsonSerializable(typeof(UpdateScheduleRequest))]
[JsonSerializable(typeof(ScheduledGraphDto))]
[JsonSerializable(typeof(ScheduledGraphListResponse))]
public partial class GraphHostingJsonSerializerContext : JsonSerializerContext
{
}
