using System.Text.Json.Serialization;
using HPD.Agent.AspNetCore.EndpointMapping;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.AspNetCore.Serialization;

/// <summary>
/// Source-generated JSON serialization context for types internal to HPD-Agent.AspNetCore.
/// Covers endpoint-local request/response types not present in HPDAgentApiJsonSerializerContext.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(WriteScoreRequest))]
[JsonSerializable(typeof(EvaluationResult))]
[JsonSerializable(typeof(ErrorResponses.ErrorsWrapper))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
internal partial class HPDAgentAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
