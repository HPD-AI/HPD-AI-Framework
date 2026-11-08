using System.Text.Json.Serialization;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.AspNetCore.Evaluations;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(WriteScoreRequest))]
[JsonSerializable(typeof(RateResponse))]
[JsonSerializable(typeof(FailureRateResponse))]
[JsonSerializable(typeof(EvaluatorCatalogItem))]
[JsonSerializable(typeof(List<EvaluatorCatalogItem>))]
[JsonSerializable(typeof(SafetyAnalyticsSummary))]
[JsonSerializable(typeof(RedTeamAnalyticsSummary))]
[JsonSerializable(typeof(RedTeamPluginCatalogItem))]
[JsonSerializable(typeof(List<RedTeamPluginCatalogItem>))]
[JsonSerializable(typeof(RedTeamStrategyCatalogItem))]
[JsonSerializable(typeof(List<RedTeamStrategyCatalogItem>))]
[JsonSerializable(typeof(RegisterStringDatasetRequest))]
[JsonSerializable(typeof(StringEvalCaseDto))]
[JsonSerializable(typeof(StringDatasetVersionResponse))]
[JsonSerializable(typeof(StringDatasetDiffResponse))]
[JsonSerializable(typeof(StringDatasetCaseChangeDto))]
[JsonSerializable(typeof(EvaluationResult))]
[JsonSerializable(typeof(ScoreRecord))]
[JsonSerializable(typeof(ScoreRecord[]))]
[JsonSerializable(typeof(List<ScoreRecord>))]
[JsonSerializable(typeof(EvaluationRunRecord))]
[JsonSerializable(typeof(List<EvaluationRunRecord>))]
[JsonSerializable(typeof(JudgeCallRecord))]
[JsonSerializable(typeof(List<JudgeCallRecord>))]
[JsonSerializable(typeof(EvaluationSource))]
[JsonSerializable(typeof(EvalPolicy))]
[JsonSerializable(typeof(RedTeamFinding))]
[JsonSerializable(typeof(List<RedTeamFinding>))]
[JsonSerializable(typeof(ScoreTrend))]
[JsonSerializable(typeof(ScoreBucket))]
[JsonSerializable(typeof(List<ScoreBucket>))]
[JsonSerializable(typeof(ScoreAggregate))]
[JsonSerializable(typeof(Dictionary<string, ScoreAggregate>))]
[JsonSerializable(typeof(ThreadComparisonResult))]
[JsonSerializable(typeof(Dictionary<string, ToolUsageSummary>))]
[JsonSerializable(typeof(ToolUsageSummary))]
[JsonSerializable(typeof(List<RiskAutonomyDataPoint>))]
[JsonSerializable(typeof(RiskAutonomyDataPoint))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(EvaluatorSummary))]
[JsonSerializable(typeof(List<EvaluatorSummary>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(DatasetRecord))]
[JsonSerializable(typeof(List<DatasetRecord>))]
[JsonSerializable(typeof(DatasetVersionRecord))]
[JsonSerializable(typeof(List<DatasetVersionRecord>))]
[JsonSerializable(typeof(EvalCase<string>))]
[JsonSerializable(typeof(List<EvalCase<string>>))]
[JsonSerializable(typeof(Dataset<string>))]
[JsonSerializable(typeof(DatasetVersionDiff<string>))]
[JsonSerializable(typeof(DatasetCaseChange<string>))]
[JsonSerializable(typeof(List<DatasetCaseChange<string>>))]
[JsonSerializable(typeof(List<StringEvalCaseDto>))]
[JsonSerializable(typeof(List<StringDatasetCaseChangeDto>))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(UsageDetails))]
internal partial class HPDAgentEvaluationJsonSerializerContext : JsonSerializerContext;
