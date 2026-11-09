// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.HpdBase;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HpdBaseEvaluationScore))]
[JsonSerializable(typeof(HpdBaseEvaluationRun))]
[JsonSerializable(typeof(ScoreRecord))]
[JsonSerializable(typeof(EvaluationRunRecord))]
[JsonSerializable(typeof(HpdBaseDatasetVersion))]
[JsonSerializable(typeof(HpdBaseDatasetHead))]
[JsonSerializable(typeof(EvaluationResult))]
[JsonSerializable(typeof(EvaluationMetric))]
[JsonSerializable(typeof(NumericMetric))]
[JsonSerializable(typeof(BooleanMetric))]
[JsonSerializable(typeof(StringMetric))]
[JsonSerializable(typeof(EvaluationMetricInterpretation))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(UsageDetails))]
[JsonSerializable(typeof(JudgeCallRecord))]
[JsonSerializable(typeof(ChatDetails))]
internal sealed partial class HpdBaseEvaluationStoreJsonContext : JsonSerializerContext;
