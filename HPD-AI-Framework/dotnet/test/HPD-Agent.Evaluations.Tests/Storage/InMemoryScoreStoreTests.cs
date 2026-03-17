// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Tests.Storage;

/// <summary>
/// Tests for InMemoryScoreStore — covering write, point queries, and analytics methods.
///
/// InMemoryScoreStore is the primary test-time IScoreStore implementation.
/// Its analytics must be correct because other tests rely on them.
/// </summary>
public sealed class InMemoryScoreStoreTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScoreRecord MakeBoolRecord(
        string evaluatorName,
        bool passed,
        string sessionId = "sess-1",
        string branchId = "branch-1",
        int turnIndex = 0,
        string agentName = "TestAgent",
        DateTimeOffset? createdAt = null,
        EvalPolicy policy = EvalPolicy.MustAlwaysPass,
        IEnumerable<ToolCallRecord>? toolCalls = null,
        string? evaluatorVersion = "1.0.0") =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = evaluatorVersion ?? "1.0.0",
            Result = new EvaluationResult(new BooleanMetric("Test") { Value = passed }),
            Source = EvaluationSource.Test,
            SessionId = sessionId,
            BranchId = branchId,
            TurnIndex = turnIndex,
            AgentName = agentName,
            Policy = policy,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Attributes = toolCalls is null ? null :
                new Dictionary<string, object> { ["tool_calls"] = toolCalls.ToArray() },
        };

    private static ScoreRecord MakeNumericRecord(
        string evaluatorName,
        double score,
        string sessionId = "sess-1",
        string branchId = "branch-1",
        int turnIndex = 0,
        string agentName = "TestAgent",
        DateTimeOffset? createdAt = null,
        string? evaluatorVersion = "1.0.0") =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = evaluatorVersion ?? "1.0.0",
            Result = new EvaluationResult(new NumericMetric("Score") { Value = score }),
            Source = EvaluationSource.Test,
            SessionId = sessionId,
            BranchId = branchId,
            TurnIndex = turnIndex,
            AgentName = agentName,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

    // ── Write / Read ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Write_ThenGetBySession_ReturnsRecord()
    {
        var store = new InMemoryScoreStore();
        var record = MakeBoolRecord("ToolWasCalled", passed: true, sessionId: "sess-abc");

        await store.WriteScoreAsync(record);

        var results = await store.GetScoresAsync(sessionId: "sess-abc").ToListAsync();
        results.Should().ContainSingle().Which.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task Write_ThenGetByEvaluatorName_ReturnsCorrectRecord()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("EvalA", true, sessionId: "s1"));
        await store.WriteScoreAsync(MakeBoolRecord("EvalB", false, sessionId: "s2"));

        var results = await store.GetScoresAsync("EvalA", from: null, to: null).ToListAsync();
        results.Should().ContainSingle().Which.EvaluatorName.Should().Be("EvalA");
    }

    [Fact]
    public async Task GetBySession_FiltersByBranchId()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true, sessionId: "s1", branchId: "b1"));
        await store.WriteScoreAsync(MakeBoolRecord("E", false, sessionId: "s1", branchId: "b2"));

        var results = await store.GetScoresAsync("s1", branchId: "b1").ToListAsync();
        results.Should().ContainSingle().Which.BranchId.Should().Be("b1");
    }

    [Fact]
    public async Task GetByEvaluatorName_TimeRangeFilter_ReturnsOnlyInRange()
    {
        var store = new InMemoryScoreStore();
        var now = DateTimeOffset.UtcNow;

        await store.WriteScoreAsync(MakeBoolRecord("E", true, createdAt: now.AddHours(-3)));
        await store.WriteScoreAsync(MakeBoolRecord("E", false, createdAt: now.AddHours(-1)));
        await store.WriteScoreAsync(MakeBoolRecord("E", true, createdAt: now.AddHours(1)));

        var results = await store.GetScoresAsync(
            "E",
            from: now.AddHours(-2),
            to: now).ToListAsync();

        results.Should().ContainSingle().Which.Result.Metrics.ContainsKey("Test");
    }

    // ── GetPassRateAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPassRate_AllPassing_ReturnsOne()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true));
        await store.WriteScoreAsync(MakeBoolRecord("E", true));
        await store.WriteScoreAsync(MakeBoolRecord("E", true));

        var rate = await store.GetPassRateAsync("E");
        rate.Should().Be(1.0);
    }

    [Fact]
    public async Task GetPassRate_HalfPassing_ReturnsHalf()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true));
        await store.WriteScoreAsync(MakeBoolRecord("E", true));
        await store.WriteScoreAsync(MakeBoolRecord("E", false));
        await store.WriteScoreAsync(MakeBoolRecord("E", false));

        var rate = await store.GetPassRateAsync("E");
        rate.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public async Task GetPassRate_NoRecords_ReturnsZero()
    {
        var store = new InMemoryScoreStore();
        var rate = await store.GetPassRateAsync("NonExistentEvaluator");
        rate.Should().Be(0.0);
    }

    // ── GetFailureRateAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetFailureRate_Complementary_ToPassRate()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true));
        await store.WriteScoreAsync(MakeBoolRecord("E", false));

        var pass = await store.GetPassRateAsync("E");
        var fail = await store.GetFailureRateAsync("E");

        (pass + fail).Should().BeApproximately(1.0, 0.01);
    }

    // ── GetTrendAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTrend_RecordsBucketedCorrectly()
    {
        var store = new InMemoryScoreStore();
        var now = DateTimeOffset.UtcNow;

        // Two records in hour 1, one in hour 2
        await store.WriteScoreAsync(MakeNumericRecord("E", 0.8, createdAt: now));
        await store.WriteScoreAsync(MakeNumericRecord("E", 0.6, createdAt: now.AddMinutes(30)));
        await store.WriteScoreAsync(MakeNumericRecord("E", 0.9, createdAt: now.AddMinutes(90)));

        var trend = await store.GetTrendAsync(
            "E",
            from: now.AddMinutes(-1),
            to: now.AddMinutes(121),
            bucketSize: TimeSpan.FromHours(1));

        trend.EvaluatorName.Should().Be("E");
        trend.Buckets.Should().HaveCount(2);

        var firstBucket = trend.Buckets[0];
        firstBucket.Average.Should().BeApproximately(0.7, 0.01); // (0.8 + 0.6) / 2
        firstBucket.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetTrend_EmptyRange_NoBuckets()
    {
        var store = new InMemoryScoreStore();
        var future = DateTimeOffset.UtcNow.AddDays(10);

        var trend = await store.GetTrendAsync(
            "E",
            from: future,
            to: future.AddHours(1),
            bucketSize: TimeSpan.FromHours(1));

        trend.Buckets.Should().BeEmpty();
    }

    // ── GetAgentComparisonAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetAgentComparison_DifferentAgents_ReturnsPerAgentAggregates()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeNumericRecord("Relevance", 0.9, agentName: "AgentA"));
        await store.WriteScoreAsync(MakeNumericRecord("Relevance", 0.8, agentName: "AgentA"));
        await store.WriteScoreAsync(MakeNumericRecord("Relevance", 0.5, agentName: "AgentB"));

        var comparison = await store.GetAgentComparisonAsync(
            "Relevance", ["AgentA", "AgentB"]);

        comparison.Should().ContainKey("AgentA");
        comparison["AgentA"].Average.Should().BeApproximately(0.85, 0.01);

        comparison.Should().ContainKey("AgentB");
        comparison["AgentB"].Average.Should().BeApproximately(0.5, 0.01);
    }

    // ── GetBranchComparisonAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetBranchComparison_TwoBranches_ReturnsPerBranchScores()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeNumericRecord("Quality", 0.9, sessionId: "s1", branchId: "b1"));
        await store.WriteScoreAsync(MakeNumericRecord("Quality", 0.6, sessionId: "s1", branchId: "b2"));

        var comparison = await store.GetBranchComparisonAsync(
            "s1", "b1", "b2", ["Quality"]);

        comparison.SessionId.Should().Be("s1");
        comparison.Branch1Scores["Quality"].Average.Should().BeApproximately(0.9, 0.01);
        comparison.Branch2Scores["Quality"].Average.Should().BeApproximately(0.6, 0.01);
    }

    // ── GetEvaluatorSummaryAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetEvaluatorSummary_MultipleEvaluators_ReturnsSummaryPerEvaluator()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("EvalA", true));
        await store.WriteScoreAsync(MakeBoolRecord("EvalA", false));
        await store.WriteScoreAsync(MakeBoolRecord("EvalB", true));

        var summaries = await store.GetEvaluatorSummaryAsync();

        summaries.Should().HaveCount(2);
        var evalA = summaries.Single(s => s.EvaluatorName == "EvalA");
        evalA.TotalCount.Should().Be(2);
        evalA.FailureCount.Should().Be(1);

        var evalB = summaries.Single(s => s.EvaluatorName == "EvalB");
        evalB.TotalCount.Should().Be(1);
        evalB.FailureCount.Should().Be(0);
    }

    // ── GetScoresByVersionAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetScoresByVersion_FiltersByVersion()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true, evaluatorVersion: "1.0.0"));
        await store.WriteScoreAsync(MakeBoolRecord("E", false, evaluatorVersion: "2.0.0"));

        var v1Records = await store.GetScoresByVersionAsync("E", "1.0.0").ToListAsync();
        v1Records.Should().ContainSingle().Which.EvaluatorVersion.Should().Be("1.0.0");

        var v2Records = await store.GetScoresByVersionAsync("E", "2.0.0").ToListAsync();
        v2Records.Should().ContainSingle().Which.EvaluatorVersion.Should().Be("2.0.0");
    }

    // ── GetRiskAutonomyDistributionAsync ──────────────────────────────────────

    [Fact]
    public async Task GetRiskAutonomyDistribution_PairedRecords_ReturnDataPoints()
    {
        var store = new InMemoryScoreStore();

        // Risk record for turn 0
        var riskRecord = new ScoreRecord
        {
            Id = "r1",
            EvaluatorName = "TurnRiskEvaluator",
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new NumericMetric("Turn Risk") { Value = 7.0 }),
            Source = EvaluationSource.Live,
            SessionId = "sess-1",
            BranchId = "branch-1",
            TurnIndex = 0,
            AgentName = "TestAgent",
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Autonomy record for same turn
        var autonomyRecord = new ScoreRecord
        {
            Id = "a1",
            EvaluatorName = "TurnAutonomyEvaluator",
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new NumericMetric("Turn Autonomy") { Value = 8.0 }),
            Source = EvaluationSource.Live,
            SessionId = "sess-1",
            BranchId = "branch-1",
            TurnIndex = 0,
            AgentName = "TestAgent",
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await store.WriteScoreAsync(riskRecord);
        await store.WriteScoreAsync(autonomyRecord);

        var points = await store.GetRiskAutonomyDistributionAsync();

        points.Should().ContainSingle();
        points[0].RiskScore.Should().Be(7.0);
        points[0].AutonomyScore.Should().Be(8.0);
        points[0].SessionId.Should().Be("sess-1");
    }

    [Fact]
    public async Task GetRiskAutonomyDistribution_UnpairedRecords_ReturnsEmpty()
    {
        var store = new InMemoryScoreStore();

        // Only risk record — no matching autonomy record
        await store.WriteScoreAsync(MakeNumericRecord("TurnRiskEvaluator", 5.0));

        var points = await store.GetRiskAutonomyDistributionAsync();
        points.Should().BeEmpty();
    }

    // ── IEvaluationResultStore methods ────────────────────────────────────────

    [Fact]
    public async Task GetLatestExecutionNames_ReturnsDistinctAgentNames()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true, agentName: "AgentA"));
        await store.WriteScoreAsync(MakeBoolRecord("E", true, agentName: "AgentA"));
        await store.WriteScoreAsync(MakeBoolRecord("E", true, agentName: "AgentB"));

        var names = await store.GetLatestExecutionNamesAsync().ToListAsync();
        names.Should().HaveCount(2)
            .And.Contain("AgentA")
            .And.Contain("AgentB");
    }

    [Fact]
    public async Task GetScenarioNames_ReturnsSessionIdsForAgent()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true, agentName: "AgentA", sessionId: "sess-1"));
        await store.WriteScoreAsync(MakeBoolRecord("E", true, agentName: "AgentA", sessionId: "sess-2"));
        await store.WriteScoreAsync(MakeBoolRecord("E", true, agentName: "AgentB", sessionId: "sess-3"));

        var scenarios = await store.GetScenarioNamesAsync("AgentA").ToListAsync();
        scenarios.Should().HaveCount(2)
            .And.Contain("sess-1")
            .And.Contain("sess-2");
    }

    [Fact]
    public async Task GetIterationNames_ReturnsBranchPlusTurnIndex()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true,
            agentName: "A", sessionId: "s", branchId: "b1", turnIndex: 0));
        await store.WriteScoreAsync(MakeBoolRecord("E", true,
            agentName: "A", sessionId: "s", branchId: "b1", turnIndex: 1));

        var iterations = await store.GetIterationNamesAsync("A", "s").ToListAsync();
        iterations.Should().Contain("b1/0").And.Contain("b1/1");
    }
}

// ── Extension helpers ─────────────────────────────────────────────────────────

file static class AsyncEnumerableTestExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
