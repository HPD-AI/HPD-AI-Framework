using System.Text.Json.Serialization;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TurnTrace))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(GoalProgressionRating))]
[JsonSerializable(typeof(MemoryAccuracyRating))]
internal partial class EvaluationJsonContext : JsonSerializerContext;
