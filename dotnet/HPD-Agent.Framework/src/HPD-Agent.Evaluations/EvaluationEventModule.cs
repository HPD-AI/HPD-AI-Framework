using System.Text.Json.Serialization;
using HPD.Agent.Serialization;
using HPD.Agent.Evaluations.Integration;

[assembly: HpdAgentEventModule("hpd.agent.evaluations", typeof(EvaluationEventJsonContext))]

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EvalScoreEvent))]
[JsonSerializable(typeof(EvalFailedEvent))]
[JsonSerializable(typeof(AnnotationRequestedEvent))]
[JsonSerializable(typeof(AnnotationResponseEvent))]
[JsonSerializable(typeof(EvalPolicyViolationEvent))]
internal sealed partial class EvaluationEventJsonContext : JsonSerializerContext;
