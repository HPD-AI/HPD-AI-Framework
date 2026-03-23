// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// In-memory IScoreStore implementation for unit tests and development.
/// All analytics queries operate over the in-memory collection — appropriate
/// for small result sets. Use SqliteScoreStore for production with analytics.
/// </summary>
public sealed class InMemoryScoreStore : IScoreStore
{
    private readonly ConcurrentBag<ScoreRecord> _records = new();

    // ── IScoreStore: Write ────────────────────────────────────────────────────

    public ValueTask WriteScoreAsync(ScoreRecord record, CancellationToken ct = default)
    {
        _records.Add(record);
        return ValueTask.CompletedTask;
    }

    // ── IScoreStore: Point queries ────────────────────────────────────────────

    public async IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string sessionId,
        string? branchId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var r in _records)
        {
            ct.ThrowIfCancellationRequested();
            if (r.SessionId == sessionId && (branchId is null || r.BranchId == branchId))
                yield return r;
        }
    }

    public async IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var r in _records)
        {
            ct.ThrowIfCancellationRequested();
            if (r.EvaluatorName != evaluatorName) continue;
            if (from.HasValue && r.CreatedAt < from.Value) continue;
            if (to.HasValue && r.CreatedAt > to.Value) continue;
            yield return r;
        }
    }

    public async IAsyncEnumerable<ScoreRecord> GetScoresByVersionAsync(
        string evaluatorName,
        string version,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var r in _records)
        {
            ct.ThrowIfCancellationRequested();
            if (r.EvaluatorName == evaluatorName && r.EvaluatorVersion == version)
                yield return r;
        }
    }

    // ── IScoreStore: Analytics ────────────────────────────────────────────────

    public ValueTask<double> GetPassRateAsync(
        string evaluatorName, DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var records = FilterRecords(evaluatorName, from, to).ToList();
        if (records.Count == 0) return ValueTask.FromResult(0.0);

        int passing = records.Count(r => IsPassingResult(r.Result));
        return ValueTask.FromResult((double)passing / records.Count);
    }

    public ValueTask<double> GetFailureRateAsync(
        string evaluatorName, DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var records = FilterRecords(evaluatorName, from, to).ToList();
        if (records.Count == 0) return ValueTask.FromResult(0.0);

        int failing = records.Count(r => !IsPassingResult(r.Result));
        return ValueTask.FromResult((double)failing / records.Count);
    }

    public ValueTask<ScoreTrend> GetTrendAsync(
        string evaluatorName, DateTimeOffset from, DateTimeOffset to,
        TimeSpan bucketSize, CancellationToken ct = default)
    {
        var records = FilterRecords(evaluatorName, from, to).ToList();
        var buckets = new List<ScoreBucket>();

        var current = from;
        while (current < to)
        {
            var next = current + bucketSize;
            var bucket = records.Where(r => r.CreatedAt >= current && r.CreatedAt < next).ToList();
            if (bucket.Count > 0)
            {
                var scores = bucket.Select(r => GetPrimaryScore(r.Result)).Where(s => s.HasValue).Select(s => s!.Value).ToList();
                if (scores.Count > 0)
                {
                    buckets.Add(new ScoreBucket(
                        Start: current,
                        Average: scores.Average(),
                        Min: scores.Min(),
                        Max: scores.Max(),
                        Count: scores.Count,
                        PassRate: (double)bucket.Count(r => IsPassingResult(r.Result)) / bucket.Count));
                }
            }
            current = next;
        }

        return ValueTask.FromResult(new ScoreTrend(evaluatorName, buckets));
    }

    public ValueTask<IDictionary<string, ScoreAggregate>> GetAgentComparisonAsync(
        string evaluatorName, IEnumerable<string> agentNames,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, ScoreAggregate>();
        foreach (var agent in agentNames)
        {
            var records = _records.Where(r =>
                r.EvaluatorName == evaluatorName && r.AgentName == agent &&
                (!from.HasValue || r.CreatedAt >= from.Value) &&
                (!to.HasValue || r.CreatedAt <= to.Value)).ToList();

            if (records.Count > 0)
            {
                var scores = records.Select(r => GetPrimaryScore(r.Result)).Where(s => s.HasValue).Select(s => s!.Value).ToList();
                result[agent] = new ScoreAggregate(
                    Average: scores.Count > 0 ? scores.Average() : 0,
                    Min: scores.Count > 0 ? scores.Min() : 0,
                    Max: scores.Count > 0 ? scores.Max() : 0,
                    Count: records.Count,
                    PassRate: (double)records.Count(r => IsPassingResult(r.Result)) / records.Count);
            }
        }
        return ValueTask.FromResult<IDictionary<string, ScoreAggregate>>(result);
    }

    public ValueTask<BranchComparisonResult> GetBranchComparisonAsync(
        string sessionId, string branchId1, string branchId2,
        IEnumerable<string> evaluatorNames, CancellationToken ct = default)
    {
        var names = evaluatorNames.ToList();
        var branch1Scores = new Dictionary<string, ScoreAggregate>();
        var branch2Scores = new Dictionary<string, ScoreAggregate>();

        foreach (var name in names)
        {
            branch1Scores[name] = ComputeAggregate(_records.Where(r => r.SessionId == sessionId && r.BranchId == branchId1 && r.EvaluatorName == name));
            branch2Scores[name] = ComputeAggregate(_records.Where(r => r.SessionId == sessionId && r.BranchId == branchId2 && r.EvaluatorName == name));
        }

        return ValueTask.FromResult(new BranchComparisonResult(sessionId, branchId1, branchId2, branch1Scores, branch2Scores));
    }

    public ValueTask<IReadOnlyList<EvaluatorSummary>> GetEvaluatorSummaryAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var grouped = _records
            .Where(r => (!from.HasValue || r.CreatedAt >= from.Value) && (!to.HasValue || r.CreatedAt <= to.Value))
            .GroupBy(r => r.EvaluatorName);

        var summaries = grouped.Select(g =>
        {
            var scores = g.Select(r => GetPrimaryScore(r.Result)).Where(s => s.HasValue).Select(s => s!.Value).ToList();
            return new EvaluatorSummary(
                EvaluatorName: g.Key,
                TotalCount: g.Count(),
                AverageScore: scores.Count > 0 ? scores.Average() : 0,
                PassRate: g.Count() > 0 ? (double)g.Count(r => IsPassingResult(r.Result)) / g.Count() : 0,
                AverageJudgeCostUsd: 0,
                AverageJudgeDuration: g.Any(r => r.JudgeDuration.HasValue)
                    ? TimeSpan.FromTicks((long)g.Where(r => r.JudgeDuration.HasValue).Average(r => r.JudgeDuration!.Value.Ticks))
                    : TimeSpan.Zero,
                FailureCount: g.Count(r => !IsPassingResult(r.Result)));
        }).ToList();

        return ValueTask.FromResult<IReadOnlyList<EvaluatorSummary>>(summaries);
    }

    public ValueTask<IDictionary<string, double>> GetCostBreakdownAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        // Aggregate judge LLM cost per evaluator from JudgeUsage.
        // Cost is estimated from token counts using a fixed approximation rate
        // since actual pricing varies by provider. Callers should treat these as
        // relative cost indicators, not exact billing figures.
        const double costPer1kTokens = 0.001; // placeholder rate

        var result = new Dictionary<string, double>();
        foreach (var record in _records)
        {
            if (from.HasValue && record.CreatedAt < from.Value) continue;
            if (to.HasValue && record.CreatedAt > to.Value) continue;
            if (record.JudgeUsage is null) continue;

            var totalTokens =
                (record.JudgeUsage.InputTokenCount ?? 0) +
                (record.JudgeUsage.OutputTokenCount ?? 0);

            if (!result.TryAdd(record.EvaluatorName, totalTokens * costPer1kTokens / 1000.0))
                result[record.EvaluatorName] += totalTokens * costPer1kTokens / 1000.0;
        }

        return ValueTask.FromResult<IDictionary<string, double>>(result);
    }

    public ValueTask<IDictionary<string, ToolUsageSummary>> GetToolUsageSummaryAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        // Aggregate tool call counts and permission-denied rates from ToolCallRecord[]
        // embedded in each ScoreRecord via TurnEvaluationContext.ToolCalls.
        // ScoreRecord doesn't carry ToolCalls directly — they're in the EvalContext
        // Attributes. We aggregate from the Metrics dictionary where EvaluationMiddleware
        // stores per-tool denial data via EvalContext.IncrementMetric.
        //
        // Fallback: scan Attributes for "tool_calls" or derive from metric keys following
        // the convention set by TurnAutonomyEvaluator ("permission_denied_rate").
        // For InMemoryScoreStore the simplest correct implementation is to accumulate
        // from all records whose Attributes contain the tool call lists.

        var totals = new Dictionary<string, (int total, int denied)>();

        foreach (var record in _records)
        {
            if (from.HasValue && record.CreatedAt < from.Value) continue;
            if (to.HasValue && record.CreatedAt > to.Value) continue;
            if (record.Attributes is null) continue;

            // Convention: EvalContext.SetAttribute("tool_calls", ToolCallRecord[])
            if (!record.Attributes.TryGetValue("tool_calls", out var raw) ||
                raw is not IEnumerable<ToolCallRecord> toolCalls)
                continue;

            foreach (var call in toolCalls)
            {
                if (!totals.TryGetValue(call.Name, out var existing))
                    existing = (0, 0);

                totals[call.Name] = (
                    existing.total + 1,
                    existing.denied + (call.WasPermissionDenied ? 1 : 0));
            }
        }

        var result = totals.ToDictionary(
            kv => kv.Key,
            kv => new ToolUsageSummary(
                TotalCalls: kv.Value.total,
                PermissionDeniedCount: kv.Value.denied,
                PermissionDeniedRate: kv.Value.total == 0
                    ? 0.0
                    : (double)kv.Value.denied / kv.Value.total));

        return ValueTask.FromResult<IDictionary<string, ToolUsageSummary>>(result);
    }

    public ValueTask<IReadOnlyList<RiskAutonomyDataPoint>> GetRiskAutonomyDistributionAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        // Find (sessionId, branchId, turnIndex) triples that have BOTH a "Turn Risk"
        // score and a "Turn Autonomy" score, then produce one data point per triple.
        var riskRecords = new Dictionary<(string, string, int), ScoreRecord>();
        var autonomyRecords = new Dictionary<(string, string, int), ScoreRecord>();

        foreach (var record in _records)
        {
            if (from.HasValue && record.CreatedAt < from.Value) continue;
            if (to.HasValue && record.CreatedAt > to.Value) continue;

            var key = (record.SessionId, record.BranchId, record.TurnIndex);

            if (record.EvaluatorName == "TurnRiskEvaluator" ||
                record.Result.Metrics.ContainsKey("Turn Risk"))
                riskRecords[key] = record;

            if (record.EvaluatorName == "TurnAutonomyEvaluator" ||
                record.Result.Metrics.ContainsKey("Turn Autonomy"))
                autonomyRecords[key] = record;
        }

        var points = new List<RiskAutonomyDataPoint>();

        foreach (var (key, riskRecord) in riskRecords)
        {
            if (!autonomyRecords.TryGetValue(key, out var autonomyRecord))
                continue;

            var riskScore = ExtractFirstNumericScore(riskRecord.Result) ?? 0.0;
            var autonomyScore = ExtractFirstNumericScore(autonomyRecord.Result) ?? 0.0;

            points.Add(new RiskAutonomyDataPoint(
                SessionId: key.Item1,
                BranchId: key.Item2,
                TurnIndex: key.Item3,
                AgentName: riskRecord.AgentName,
                RiskScore: riskScore,
                AutonomyScore: autonomyScore,
                CreatedAt: riskRecord.CreatedAt));
        }

        return ValueTask.FromResult<IReadOnlyList<RiskAutonomyDataPoint>>(points);

        static double? ExtractFirstNumericScore(EvaluationResult result)
        {
            foreach (var (_, metric) in result.Metrics)
                if (metric is NumericMetric nm && nm.Value.HasValue)
                    return nm.Value.Value;
            return null;
        }
    }

    // ── IEvaluationResultStore (MS 9.6.0 interface) ───────────────────────────

    /// <summary>
    /// Deletes results for a specific execution/scenario/iteration combination.
    /// All params nullable — null means "match all". InMemoryScoreStore is test-only;
    /// ConcurrentBag doesn't support targeted removal, so this is a no-op.
    /// </summary>
    public ValueTask DeleteResultsAsync(string? executionName, string? scenarioName, string? iterationName, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    public IAsyncEnumerable<string> GetLatestExecutionNamesAsync(int? maxCount = null, CancellationToken ct = default)
    {
        var query = _records.Select(r => r.AgentName).Distinct();
        if (maxCount.HasValue) query = query.Take(maxCount.Value);
        return query.ToAsyncEnumerable();
    }

    public IAsyncEnumerable<string> GetScenarioNamesAsync(string? executionName, CancellationToken ct = default)
        => _records
            .Where(r => executionName is null || r.AgentName == executionName)
            .Select(r => r.SessionId)
            .Distinct()
            .ToAsyncEnumerable();

    public IAsyncEnumerable<string> GetIterationNamesAsync(string? executionName, string? scenarioName, CancellationToken ct = default)
        => _records
            .Where(r => (executionName is null || r.AgentName == executionName) &&
                        (scenarioName is null || r.SessionId == scenarioName))
            .Select(r => $"{r.BranchId}/{r.TurnIndex}")
            .Distinct()
            .ToAsyncEnumerable();

    public IAsyncEnumerable<ScenarioRunResult> ReadResultsAsync(string? executionName, string? scenarioName, string? iterationName, CancellationToken ct = default)
        => Array.Empty<ScenarioRunResult>().ToAsyncEnumerable();

    public ValueTask WriteResultsAsync(IEnumerable<ScenarioRunResult> results, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    // ── Private helpers ───────────────────────────────────────────────────────

    private IEnumerable<ScoreRecord> FilterRecords(string evaluatorName, DateTimeOffset? from, DateTimeOffset? to)
        => _records.Where(r =>
            r.EvaluatorName == evaluatorName &&
            (!from.HasValue || r.CreatedAt >= from.Value) &&
            (!to.HasValue || r.CreatedAt <= to.Value));

    private static bool IsPassingResult(EvaluationResult result)
    {
        foreach (var (_, metric) in result.Metrics)
        {
            if (metric is BooleanMetric bm && bm.Value == false) return false;
            if (metric is NumericMetric nm && nm.Value.HasValue && nm.Value.Value < 0.5) return false;
        }
        return true;
    }

    private static double? GetPrimaryScore(EvaluationResult result)
    {
        var first = result.Metrics.FirstOrDefault();
        return first.Value switch
        {
            NumericMetric nm => nm.Value,
            BooleanMetric bm => bm.Value.HasValue ? (bm.Value.Value ? 1.0 : 0.0) : null,
            _ => null,
        };
    }

    private static ScoreAggregate ComputeAggregate(IEnumerable<ScoreRecord> records)
    {
        var list = records.ToList();
        if (list.Count == 0) return new ScoreAggregate(0, 0, 0, 0, 0);
        var scores = list.Select(r => GetPrimaryScore(r.Result)).Where(s => s.HasValue).Select(s => s!.Value).ToList();
        return new ScoreAggregate(
            Average: scores.Count > 0 ? scores.Average() : 0,
            Min: scores.Count > 0 ? scores.Min() : 0,
            Max: scores.Count > 0 ? scores.Max() : 0,
            Count: list.Count,
            PassRate: (double)list.Count(r => IsPassingResult(r.Result)) / list.Count);
    }
}

/// <summary>Helper to convert IEnumerable to IAsyncEnumerable for the MS interface.</summary>
internal static class AsyncEnumerableExtensions
{
    internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
            yield return item;
    }
}
