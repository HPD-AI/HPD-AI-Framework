using System.Text.Json.Serialization;
using HPD.Agent.AspNetCore.EndpointMapping;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;
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
// Endpoint request/response types
[JsonSerializable(typeof(WriteScoreRequest))]
[JsonSerializable(typeof(EvaluationResult))]
// Error response collections
[JsonSerializable(typeof(Dictionary<string, string[]>))]
// Evaluation score endpoints
[JsonSerializable(typeof(ScoreRecord))]
[JsonSerializable(typeof(ScoreRecord[]))]
[JsonSerializable(typeof(List<ScoreRecord>))]
[JsonSerializable(typeof(EvaluationSource))]
[JsonSerializable(typeof(EvalPolicy))]
// Evaluation score return types (collections of objects from analytics endpoints)
[JsonSerializable(typeof(object))]
internal partial class HPDAgentAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
