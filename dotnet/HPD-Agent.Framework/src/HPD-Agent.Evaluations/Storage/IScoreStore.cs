// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Extends the MS IEvaluationResultStore with HPD-specific score analytics and
/// HPD-native full run records.
///
/// ScoreRecord is the per-evaluator analytic row. EvaluationRunRecord is the
/// full reporting payload for one evaluated case/turn, equivalent to MS
/// ScenarioRunResult plus HPD provenance.
/// </summary>
public interface IScoreStore : IEvaluationResultStore
{
    // ── Write ─────────────────────────────────────────────────────────────────

    ValueTask WriteScoreAsync(ScoreRecord record, CancellationToken ct = default);

    ValueTask WriteRunAsync(EvaluationRunRecord record, CancellationToken ct = default);

    // ── Point queries ─────────────────────────────────────────────────────────

    IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string sessionId,
        string? threadId = null,
        CancellationToken ct = default);

    IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    IAsyncEnumerable<EvaluationRunRecord> GetRunsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        CancellationToken ct = default);

    ValueTask DeleteRunsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> GetLatestRunExecutionNamesAsync(
        int? count = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> GetRunScenarioNamesAsync(
        string executionName,
        CancellationToken ct = default);

    IAsyncEnumerable<string> GetRunIterationNamesAsync(
        string executionName,
        string scenarioName,
        CancellationToken ct = default);

    // ── Analytics ─────────────────────────────────────────────────────────────

    ValueTask<ScoreTrend> GetTrendAsync(
        string evaluatorName,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan bucketSize,
        CancellationToken ct = default);

    ValueTask<double> GetPassRateAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    ValueTask<IDictionary<string, ScoreAggregate>> GetAgentComparisonAsync(
        string evaluatorName,
        IEnumerable<string> agentNames,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    ValueTask<ThreadComparisonResult> GetThreadComparisonAsync(
        string sessionId,
        string threadId1,
        string threadId2,
        IEnumerable<string> evaluatorNames,
        CancellationToken ct = default);

    ValueTask<IReadOnlyList<EvaluatorSummary>> GetEvaluatorSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    ValueTask<double> GetFailureRateAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    ValueTask<IDictionary<string, double>> GetCostBreakdownAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    // ── Evaluator versioning ──────────────────────────────────────────────────

    IAsyncEnumerable<ScoreRecord> GetScoresByVersionAsync(
        string evaluatorName,
        string version,
        CancellationToken ct = default);

    // ── Tool usage analytics ──────────────────────────────────────────────────

    /// <summary>
    /// Aggregates tool call counts and permission-denied rates across stored ScoreRecords.
    /// Key = tool name. Useful for identifying which tools fail most often or are
    /// most frequently permission-denied.
    /// </summary>
    ValueTask<IDictionary<string, ToolUsageSummary>> GetToolUsageSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    // ── Risk / autonomy joint distribution ────────────────────────────────────

    /// <summary>
    /// Returns paired risk and autonomy scores per turn, enabling the risk/autonomy
    /// scatter plot described in Anthropic's "Measuring AI Agent Autonomy in Practice" (2026).
    /// Only returns data points where both a "Turn Risk" score and a "Turn Autonomy" score
    /// exist for the same (sessionId, threadId, turnIndex) triple.
    /// </summary>
    ValueTask<IReadOnlyList<RiskAutonomyDataPoint>> GetRiskAutonomyDistributionAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    // ── Red-team analytics ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the fraction of red-team score records where AttackSucceeded is true.
    /// Records without AttackSucceeded are ignored.
    /// </summary>
    ValueTask<double> GetAttackSuccessRateAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    /// <summary>Returns attack success rate grouped by RedTeamPluginId.</summary>
    ValueTask<IDictionary<string, double>> GetAttackSuccessRateByPluginAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    /// <summary>Returns attack success rate grouped by RedTeamStrategyId.</summary>
    ValueTask<IDictionary<string, double>> GetAttackSuccessRateByStrategyAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    /// <summary>Returns red-team findings for records where AttackSucceeded is true.</summary>
    ValueTask<IReadOnlyList<RedTeamFinding>> GetRedTeamFindingsAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);
}

// ── Supporting value types ────────────────────────────────────────────────────

/// <summary>
/// Paired risk and autonomy scores for a single turn, enabling scatter-plot monitoring.
/// </summary>
public sealed record RiskAutonomyDataPoint(
    string SessionId,
    string ThreadId,
    int TurnIndex,
    string AgentName,
    /// <summary>Score from TurnRiskEvaluator (1–10). Higher = more potential for harm.</summary>
    double RiskScore,
    /// <summary>Score from TurnAutonomyEvaluator (1–10). Higher = more autonomous.</summary>
    double AutonomyScore,
    DateTimeOffset CreatedAt);

/// <summary>Persisted attack finding derived from a red-team score record.</summary>
public sealed record RedTeamFinding(
    string ScoreRecordId,
    string? PluginId,
    string? StrategyId,
    string? Category,
    string? Severity,
    string? AttackGoal,
    bool AttackSucceeded,
    string EvaluatorName,
    string SessionId,
    string ThreadId,
    int TurnIndex,
    DateTimeOffset CreatedAt);

/// <summary>Aggregated tool usage statistics across stored turns.</summary>
public sealed record ToolUsageSummary(
    /// <summary>Total calls across all stored turns in the requested time range.</summary>
    int TotalCalls,
    /// <summary>Number of calls where WasPermissionDenied = true.</summary>
    int PermissionDeniedCount,
    /// <summary>PermissionDeniedCount / TotalCalls. 0.0 when TotalCalls == 0.</summary>
    double PermissionDeniedRate);

/// <summary>Score trend over a time range, bucketed by a configurable interval.</summary>
public sealed record ScoreTrend(
    string EvaluatorName,
    IReadOnlyList<ScoreBucket> Buckets);

public sealed record ScoreBucket(
    DateTimeOffset Start,
    double Average,
    double Min,
    double Max,
    int Count,
    double PassRate);

/// <summary>Aggregate statistics for a single evaluator across a set of score records.</summary>
public sealed record ScoreAggregate(
    double Average,
    double Min,
    double Max,
    int Count,
    double PassRate);

/// <summary>Summary statistics for one evaluator across all stored scores.</summary>
public sealed record EvaluatorSummary(
    string EvaluatorName,
    int TotalCount,
    double AverageScore,
    double PassRate,
    double AverageJudgeCostUsd,
    TimeSpan AverageJudgeDuration,
    int FailureCount);

/// <summary>
/// Comparison of two threads within the same session across named evaluators.
/// </summary>
public sealed record ThreadComparisonResult(
    string SessionId,
    string ThreadId1,
    string ThreadId2,
    IReadOnlyDictionary<string, ScoreAggregate> Thread1Scores,
    IReadOnlyDictionary<string, ScoreAggregate> Thread2Scores);
