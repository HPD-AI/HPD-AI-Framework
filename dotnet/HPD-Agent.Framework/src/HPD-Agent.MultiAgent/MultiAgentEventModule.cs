using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.agent.multiagent", typeof(HPD.MultiAgent.MultiAgentEventJsonContext))]

namespace HPD.MultiAgent;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkflowStartedEvent))]
[JsonSerializable(typeof(WorkflowCompletedEvent))]
[JsonSerializable(typeof(WorkflowAgentStartedEvent))]
[JsonSerializable(typeof(WorkflowAgentCompletedEvent))]
[JsonSerializable(typeof(WorkflowAgentSkippedEvent))]
[JsonSerializable(typeof(WorkflowEdgeTraversedEvent))]
[JsonSerializable(typeof(WorkflowLayerStartedEvent))]
[JsonSerializable(typeof(WorkflowLayerCompletedEvent))]
[JsonSerializable(typeof(WorkflowDiagnosticEvent))]
internal sealed partial class MultiAgentEventJsonContext : JsonSerializerContext;
