// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Base;
using System.Text.Json.Serialization;

namespace HPD.Agent.Evaluations.HpdBase;

[BaseCollection(
    "hpd.evaluation.scores",
    typeof(HpdBaseEvaluationStoreJsonContext),
    Name = "HPD evaluation scores")]
public sealed partial record HpdBaseEvaluationScore
{
    [BaseField("evaluator-name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string EvaluatorName { get; init; }

    [BaseField("evaluator-version", Operators = BaseFieldOperator.Equal)]
    public required string EvaluatorVersion { get; init; }

    [BaseField("session-id", Operators = BaseFieldOperator.Equal)]
    public required string SessionId { get; init; }

    [BaseField("thread-id", Operators = BaseFieldOperator.Equal)]
    public required string ThreadId { get; init; }

    [BaseField("turn-index")]
    public required int TurnIndex { get; init; }

    [BaseField("agent-name", Operators = BaseFieldOperator.Equal)]
    public required string AgentName { get; init; }

    [BaseField("source", Operators = BaseFieldOperator.Equal)]
    public required string Source { get; init; }

    [BaseField("policy", Operators = BaseFieldOperator.Equal)]
    public required string Policy { get; init; }

    [BaseField("primary-score")]
    public required double? PrimaryScore { get; init; }

    [BaseField("passed")]
    public required bool Passed { get; init; }

    [BaseField("provider-key")]
    public string? ProviderKey { get; init; }

    [BaseField("model-id")]
    public string? ModelId { get; init; }

    [BaseField("dataset-id", Operators = BaseFieldOperator.Equal)]
    public string? DatasetId { get; init; }

    [BaseField("red-team-plugin-id", Operators = BaseFieldOperator.Equal)]
    public string? RedTeamPluginId { get; init; }

    [BaseField("red-team-strategy-id", Operators = BaseFieldOperator.Equal)]
    public string? RedTeamStrategyId { get; init; }

    [BaseField("attack-succeeded")]
    public bool? AttackSucceeded { get; init; }

    [BaseField("score.created-at", Operators = BaseFieldOperator.Order)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    [BaseField("score.payload")]
    public required string Payload { get; init; }
}

[BaseCollection(
    "hpd.evaluation.runs",
    typeof(HpdBaseEvaluationStoreJsonContext),
    Name = "HPD evaluation runs")]
public sealed partial record HpdBaseEvaluationRun
{
    [BaseField("execution-name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string ExecutionName { get; init; }

    [BaseField("scenario-name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string ScenarioName { get; init; }

    [BaseField("iteration-name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string IterationName { get; init; }

    [BaseField("run.created-at", Operators = BaseFieldOperator.Order)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    [BaseField("run.payload")]
    public required string Payload { get; init; }
}
