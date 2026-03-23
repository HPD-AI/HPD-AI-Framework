// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Extends the MS IEvaluationResultStore with HPD-specific analytics query methods.
/// Implementations: InMemoryScoreStore (tests/dev), SqliteScoreStore (production).
///
/// IEvaluationResultStore method mapping:
///   DeleteResultsAsync(executionName)       → delete ScoreRecords where AgentName == executionName
///   GetLatestExecutionNamesAsync(maxCount)  → most recent distinct AgentName values by CreatedAt
///   GetScenarioNamesAsync(executionName)    → distinct SessionId values for that AgentName
///   GetIterationNamesAsync(exec, scenario)  → "(BranchId)/(TurnIndex)" strings for agent+session
/// </summary>
public interface IScoreStore : IEvaluationResultStore
{
    // ── Write ─────────────────────────────────────────────────────────────────

    ValueTask WriteScoreAsync(ScoreRecord record, CancellationToken ct = default);

    // ── Point queries ─────────────────────────────────────────────────────────

    IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string sessionId,
        string? branchId = null,
        CancellationToken ct = default);

    IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
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

    ValueTask<BranchComparisonResult> GetBranchComparisonAsync(
        string sessionId,
        string branchId1,
        string branchId2,
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
    /// exist for the same (sessionId, branchId, turnIndex) triple.
    /// </summary>
    ValueTask<IReadOnlyList<RiskAutonomyDataPoint>> GetRiskAutonomyDistributionAsync(
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
    string BranchId,
    int TurnIndex,
    string AgentName,
    /// <summary>Score from TurnRiskEvaluator (1–10). Higher = more potential for harm.</summary>
    double RiskScore,
    /// <summary>Score from TurnAutonomyEvaluator (1–10). Higher = more autonomous.</summary>
    double AutonomyScore,
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
/// Comparison of two branches within the same session across named evaluators.
/// </summary>
public sealed record BranchComparisonResult(
    string SessionId,
    string BranchId1,
    string BranchId2,
    IReadOnlyDictionary<string, ScoreAggregate> Branch1Scores,
    IReadOnlyDictionary<string, ScoreAggregate> Branch2Scores);
