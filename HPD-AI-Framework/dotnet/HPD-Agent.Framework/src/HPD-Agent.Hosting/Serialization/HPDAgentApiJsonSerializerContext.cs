using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Serialization;

/// <summary>
/// Source-generated JSON serialization context for all HPD-Agent hosting DTOs.
/// Enables Native AOT compilation by eliminating runtime reflection.
///
/// Used by:
/// - ErrorResponses (in AspNetCore) for HTTP error responses during data access operations
/// - SseEventHandler streaming for agent events (via chain in AspNetCore JsonOptionsSetup)
/// - Minimal API endpoints that return these DTOs
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
// Session DTOs
[JsonSerializable(typeof(SessionDto))]
[JsonSerializable(typeof(SessionDto[]))]
[JsonSerializable(typeof(List<SessionDto>))]
// Branch DTOs
[JsonSerializable(typeof(BranchDto))]
[JsonSerializable(typeof(BranchDto[]))]
[JsonSerializable(typeof(List<BranchDto>))]
[JsonSerializable(typeof(BranchRunDto))]
[JsonSerializable(typeof(BranchRunDto[]))]
[JsonSerializable(typeof(List<BranchRunDto>))]
[JsonSerializable(typeof(BranchRunErrorDto))]
[JsonSerializable(typeof(BranchRunBackgroundOperationDto))]
[JsonSerializable(typeof(BranchRunBackgroundTaskDto))]
[JsonSerializable(typeof(List<BranchRunBackgroundTaskDto>))]
// Branch event DTOs
[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(AgentEvent[]))]
[JsonSerializable(typeof(List<AgentEvent>))]
// Content DTOs
[JsonSerializable(typeof(ContentDto))]
[JsonSerializable(typeof(ContentDto[]))]
[JsonSerializable(typeof(List<ContentDto>))]
// Request DTOs
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(UpdateSessionRequest))]
[JsonSerializable(typeof(SearchSessionsRequest))]
[JsonSerializable(typeof(CreateBranchRequest))]
[JsonSerializable(typeof(UpdateBranchRequest))]
[JsonSerializable(typeof(ForkBranchRequest))]
[JsonSerializable(typeof(StreamTextRequest))]
// Note: Agent input/output events are covered by AgentEventJsonContext.
[JsonSerializable(typeof(ClientToolContentDto))]
[JsonSerializable(typeof(ClientToolContentDto[]))]
[JsonSerializable(typeof(List<ClientToolContentDto>))]
// Agent DTOs — AgentConfig is covered by HPDJsonContext in the chain
[JsonSerializable(typeof(AgentSummaryDto))]
[JsonSerializable(typeof(AgentSummaryDto[]))]
[JsonSerializable(typeof(List<AgentSummaryDto>))]
[JsonSerializable(typeof(StoredAgentDto))]
[JsonSerializable(typeof(CreateAgentRequest))]
[JsonSerializable(typeof(UpdateAgentRequest))]
// ReasoningOptions — used by AgentRunConfig when events are serialized through AgentEventJsonContext.
[JsonSerializable(typeof(HPD.Agent.ReasoningOptions))]
[JsonSerializable(typeof(HPD.Agent.ReasoningEffort))]
[JsonSerializable(typeof(HPD.Agent.ReasoningOutput))]
// Primitive collections used in responses
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
public partial class HPDAgentApiJsonSerializerContext : JsonSerializerContext
{
}
