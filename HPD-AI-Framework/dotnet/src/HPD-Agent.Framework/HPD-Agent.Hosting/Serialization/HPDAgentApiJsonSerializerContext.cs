using System.Text.Json.Serialization;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Serialization;

/// <summary>
/// Source-generated JSON serialization context for all HPD-Agent hosting DTOs.
/// Enables Native AOT compilation by eliminating runtime reflection.
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
// Message DTOs
[JsonSerializable(typeof(MessageDto))]
[JsonSerializable(typeof(MessageDto[]))]
[JsonSerializable(typeof(List<MessageDto>))]
// Asset DTOs
[JsonSerializable(typeof(AssetDto))]
[JsonSerializable(typeof(AssetDto[]))]
[JsonSerializable(typeof(List<AssetDto>))]
// Request DTOs
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(UpdateSessionRequest))]
[JsonSerializable(typeof(SearchSessionsRequest))]
[JsonSerializable(typeof(CreateBranchRequest))]
[JsonSerializable(typeof(UpdateBranchRequest))]
[JsonSerializable(typeof(ForkBranchRequest))]
[JsonSerializable(typeof(StreamRequest))]
[JsonSerializable(typeof(StreamMessage))]
[JsonSerializable(typeof(StreamMessage[]))]
[JsonSerializable(typeof(List<StreamMessage>))]
[JsonSerializable(typeof(StreamRunConfigDto))]
[JsonSerializable(typeof(ChatRunConfigDto))]
[JsonSerializable(typeof(PermissionResponseRequest))]
[JsonSerializable(typeof(ClientToolResponseRequest))]
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
// ReasoningOptions — used by ChatRunConfigDto
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
