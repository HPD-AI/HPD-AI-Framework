using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.MultiAgent.AspNetCore.EndpointMapping;

namespace HPD.Agent.MultiAgent.AspNetCore.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MultiAgentWorkflowListResponse))]
[JsonSerializable(typeof(MultiAgentWorkflowSummaryDto))]
[JsonSerializable(typeof(List<MultiAgentWorkflowSummaryDto>))]
[JsonSerializable(typeof(MultiAgentRunListResponse))]
[JsonSerializable(typeof(MultiAgentRunEventDto))]
[JsonSerializable(typeof(MultiAgentApprovalResponseRequest))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(JsonElement))]
internal partial class HPDMultiAgentAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
