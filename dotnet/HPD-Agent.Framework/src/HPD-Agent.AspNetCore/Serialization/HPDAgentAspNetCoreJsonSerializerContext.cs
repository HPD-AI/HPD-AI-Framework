using System.Text.Json.Serialization;
using HPD.Agent.AspNetCore.EndpointMapping;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.AspNetCore.Serialization;

/// <summary>
/// Source-generated JSON serialization context for types internal to HPD-Agent.AspNetCore.
/// Covers endpoint-local request/response types not present in HPDAgentApiJsonSerializerContext.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
// Endpoint error response collections
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]
[JsonSerializable(typeof(MiddlewareResponseConflictResponse))]
// Microsoft.Extensions.AI payloads inside run/judge records
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(UsageDetails))]
[JsonSerializable(typeof(AgentEventCatalog))]
[JsonSerializable(typeof(AgentEventCatalogEntry))]
[JsonSerializable(typeof(AgentInputCatalog))]
[JsonSerializable(typeof(AgentInputCatalogEntry))]
[JsonSerializable(typeof(RuntimeAgentExtensionCatalog))]
[JsonSerializable(typeof(RuntimeAgentExtensionDescriptor))]
[JsonSerializable(typeof(RuntimeAgentExtensionSchema))]
[JsonSerializable(typeof(AgentEventRoute))]
internal partial class HPDAgentAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
