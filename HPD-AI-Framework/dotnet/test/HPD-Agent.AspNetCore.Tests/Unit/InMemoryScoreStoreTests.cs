// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Unit tests for InMemoryScoreStore — verifies all IScoreStore analytics methods
/// using direct in-process calls (no HTTP layer).
/// </summary>
public class InMemoryScoreStoreTests
{
    private readonly InMemoryScoreStore _store = new();

    // ── Score builder helpers ────────────────────────────────────────────────

    private static ScoreRecord MakeBool(
        string evaluatorName = "TestEval",
        string evaluatorVersion = "1.0",
        string sessionId = "s1",
        string branchId = "main",
        int turnIndex = 0,
        string agentName = "agent",
        bool passing = true,
        DateTimeOffset? createdAt = null)
    {
        var metric = new BooleanMetric(passing ? "Pass" : "Fail") { Value = passing };
        var result = new EvaluationResult([metric]);
        return new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = evaluatorVersion,
            Result = result,
            Source = EvaluationSource.Test,
            SessionId = sessionId,
            BranchId = branchId,
            TurnIndex = turnIndex,
            AgentName = agentName,
            TurnDuration = TimeSpan.FromSeconds(1),
            SamplingRate = 1.0,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }

    private static ScoreRecord MakeNumeric(
        string evaluatorName = "TestEval",
        string sessionId = "s1",
        string branchId = "main",
        int turnIndex = 0,
        string agentName = "agent",
        double score = 5.0,
        DateTimeOffset? createdAt = null)
    {
        var metric = new NumericMetric(evaluatorName) { Value = score };
        var result = new EvaluationResult([metric]);
        return new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = "1.0",
            Result = result,
            Source = EvaluationSource.Test,
            SessionId = sessionId,
            BranchId = branchId,
            TurnIndex = turnIndex,
            AgentName = agentName,
            TurnDuration = TimeSpan.FromSeconds(1),
            SamplingRate = 1.0,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }

    private async Task<List<ScoreRecord>> ToListAsync(IAsyncEnumerable<ScoreRecord> source)
    {
        var list = new List<ScoreRecord>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }

    // Disambiguate the two GetScoresAsync overloads
    private Task<List<ScoreRecord>> ByEvaluator(string name, DateTimeOffset? from = null, DateTimeOffset? to = null)
        => ToListAsync(_store.GetScoresAsync(evaluatorName: name, from: from, to: to));

    private Task<List<ScoreRecord>> BySession(string sessionId, string? branchId = null)
        => ToListAsync(_store.GetScoresAsync(sessionId: sessionId, branchId: branchId));

    // =========================================================================
    // Category A — WriteScoreAsync / GetScoresAsync (by evaluator)
    // =========================================================================

    [Fact]
    public async Task WriteScore_And_GetByEvaluator_RoundTrips()
    {
        var record = MakeBool("RoundtripEval", sessionId: "rt-s1");
        await _store.WriteScoreAsync(record);

        var results = await ByEvaluator("RoundtripEval");

        results.Should().HaveCountGreaterThanOrEqualTo(1);
        var found = results.First(r => r.Id == record.Id);
        found.EvaluatorName.Should().Be("RoundtripEval");
        found.SessionId.Should().Be("rt-s1");
        found.AgentName.Should().Be("agent");
    }

    [Fact]
    public async Task GetScoresByEvaluator_FiltersToMatchingEvaluatorOnly()
    {
        await _store.WriteScoreAsync(MakeBool("FilterEval_Alpha", sessionId: "fe-s1"));
        await _store.WriteScoreAsync(MakeBool("FilterEval_Alpha", sessionId: "fe-s2"));
        await _store.WriteScoreAsync(MakeBool("FilterEval_Beta", sessionId: "fe-s3"));

        var results = await ByEvaluator("FilterEval_Alpha");

        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().OnlyContain(r => r.EvaluatorName == "FilterEval_Alpha");
    }

    [Fact]
    public async Task GetScoresByEvaluator_DateRange_From_FiltersOldRecords()
    {
        var old = DateTimeOffset.UtcNow.AddHours(-3);
        var recent = DateTimeOffset.UtcNow.AddMinutes(-10);
        await _store.WriteScoreAsync(MakeBool("DateEval_From", sessionId: "df-s1", createdAt: old));
        await _store.WriteScoreAsync(MakeBool("DateEval_From", sessionId: "df-s2", createdAt: recent));

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var results = await ByEvaluator("DateEval_From", from: from);

        results.Should().HaveCount(1);
        results[0].SessionId.Should().Be("df-s2");
    }

    [Fact]
    public async Task GetScoresByEvaluator_DateRange_To_ExcludesRecordsAfterCutoff()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-30);
        var after = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _store.WriteScoreAsync(MakeBool("DateEval_To", sessionId: "dt-s1", createdAt: before));
        await _store.WriteScoreAsync(MakeBool("DateEval_To", sessionId: "dt-s2", createdAt: after));

        var to = DateTimeOffset.UtcNow.AddMinutes(-15);
        var results = await ByEvaluator("DateEval_To", to: to);

        results.Should().HaveCount(1);
        results[0].SessionId.Should().Be("dt-s1");
    }

    [Fact]
    public async Task GetScoresByEvaluator_ReturnsEmpty_WhenNoMatchingEvaluator()
    {
        var results = await ByEvaluator("EvalThatNeverExistsXYZ123");

        results.Should().BeEmpty();
    }

    // =========================================================================
    // Category B — GetScoresAsync (by session/branch)
    // =========================================================================

    [Fact]
    public async Task GetScoresBySession_Returns_AllBranchesForSession()
    {
        const string sid = "sess-multi";
        await _store.WriteScoreAsync(MakeBool(sessionId: sid, branchId: "main"));
        await _store.WriteScoreAsync(MakeBool(sessionId: sid, branchId: "fork-1"));
        await _store.WriteScoreAsync(MakeBool(sessionId: "OTHER-sess-multi", branchId: "main"));

        var results = await BySession(sid);

        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().OnlyContain(r => r.SessionId == sid);
    }

    [Fact]
    public async Task GetScoresBySession_FiltersToSpecificBranch()
    {
        const string sid = "sess-specific";
        await _store.WriteScoreAsync(MakeBool(sessionId: sid, branchId: "main"));
        await _store.WriteScoreAsync(MakeBool(sessionId: sid, branchId: "fork-1"));

        var results = await BySession(sid, branchId: "main");

        results.Should().HaveCountGreaterThanOrEqualTo(1);
        results.Should().OnlyContain(r => r.BranchId == "main");
    }

    [Fact]
    public async Task GetScoresBySession_ReturnsEmpty_ForUnknownSession()
    {
        var results = await BySession("session-that-does-not-exist-xyz");

        results.Should().BeEmpty();
    }

    // =========================================================================
    // Category C — GetScoresByVersionAsync
    // =========================================================================

    [Fact]
    public async Task GetScoresByVersion_FiltersToMatchingVersion()
    {
        await _store.WriteScoreAsync(MakeBool("VerEval", evaluatorVersion: "1.0", sessionId: "ver-s1"));
        await _store.WriteScoreAsync(MakeBool("VerEval", evaluatorVersion: "2.0", sessionId: "ver-s2"));

        var results = await ToListAsync(_store.GetScoresByVersionAsync("VerEval", "1.0"));

        results.Should().HaveCountGreaterThanOrEqualTo(1);
        results.Should().OnlyContain(r => r.EvaluatorVersion == "1.0");
    }

    [Fact]
    public async Task GetScoresByVersion_ReturnsEmpty_WhenNoMatch()
    {
        var results = await ToListAsync(_store.GetScoresByVersionAsync("VerEval", "99.0"));

        results.Should().BeEmpty();
    }

    // =========================================================================
    // Category D — GetEvaluatorSummaryAsync
    // =========================================================================

    [Fact]
    public async Task GetEvaluatorSummary_AggregatesCountsCorrectly()
    {
        const string evalA = "SummEval_A";
        const string evalB = "SummEval_B";
        await _store.WriteScoreAsync(MakeBool(evalA, sessionId: "sum-s1", passing: true));
        await _store.WriteScoreAsync(MakeBool(evalA, sessionId: "sum-s2", passing: false));
        await _store.WriteScoreAsync(MakeBool(evalB, sessionId: "sum-s3", passing: true));

        var summaries = await _store.GetEvaluatorSummaryAsync();

        var listA = summaries.FirstOrDefault(s => s.EvaluatorName == evalA);
        var listB = summaries.FirstOrDefault(s => s.EvaluatorName == evalB);
        listA.Should().NotBeNull();
        listB.Should().NotBeNull();
        listA!.TotalCount.Should().Be(2);
        listB!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetEvaluatorSummary_ReturnsEmpty_WhenNoScores()
    {
        var fresh = new InMemoryScoreStore();

        var summaries = await fresh.GetEvaluatorSummaryAsync();

        summaries.Should().BeEmpty();
    }

    // =========================================================================
    // Category E — GetPassRateAsync / GetFailureRateAsync
    // =========================================================================

    [Fact]
    public async Task GetPassRate_ReturnsCorrectFraction()
    {
        const string name = "PassRateEval";
        // 3 passing, 1 failing → 0.75
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "pr-1", passing: true));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "pr-2", passing: true));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "pr-3", passing: true));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "pr-4", passing: false));

        var rate = await _store.GetPassRateAsync(name);

        rate.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public async Task GetPassRate_ReturnsZero_WhenAllFail()
    {
        const string name = "AllFailEval";
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "af-1", passing: false));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "af-2", passing: false));

        var rate = await _store.GetPassRateAsync(name);

        rate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetPassRate_ReturnsOne_WhenAllPass()
    {
        const string name = "AllPassEval";
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "ap-1", passing: true));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "ap-2", passing: true));

        var rate = await _store.GetPassRateAsync(name);

        rate.Should().Be(1.0);
    }

    [Fact]
    public async Task GetFailureRate_ReturnsCorrectFraction()
    {
        const string name = "FailRateEval";
        // InMemoryScoreStore uses !IsPassingResult for failure rate
        // 2 failing, 2 passing → 0.5
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "fr-1", passing: false));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "fr-2", passing: false));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "fr-3", passing: true));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "fr-4", passing: true));

        var rate = await _store.GetFailureRateAsync(name);

        rate.Should().BeApproximately(0.5, 0.001);
    }

    // =========================================================================
    // Category F — GetTrendAsync
    // =========================================================================

    [Fact]
    public async Task GetTrend_BucketsBySizeCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        const string name = "TrendBucketEval";

        // 2 scores in hour 1 (90 and 80 minutes ago)
        await _store.WriteScoreAsync(MakeNumeric(name, sessionId: "tb-s1", score: 3.0, createdAt: now.AddMinutes(-90)));
        await _store.WriteScoreAsync(MakeNumeric(name, sessionId: "tb-s2", score: 4.0, createdAt: now.AddMinutes(-80)));
        // 3 scores in hour 2 (60, 50, 40 minutes ago)
        await _store.WriteScoreAsync(MakeNumeric(name, sessionId: "tb-s3", score: 5.0, createdAt: now.AddMinutes(-60)));
        await _store.WriteScoreAsync(MakeNumeric(name, sessionId: "tb-s4", score: 6.0, createdAt: now.AddMinutes(-50)));
        await _store.WriteScoreAsync(MakeNumeric(name, sessionId: "tb-s5", score: 7.0, createdAt: now.AddMinutes(-40)));

        var trend = await _store.GetTrendAsync(name, now.AddHours(-2), now, TimeSpan.FromHours(1));

        trend.EvaluatorName.Should().Be(name);
        trend.Buckets.Should().HaveCountGreaterThanOrEqualTo(1);
        trend.Buckets.Sum(b => b.Count).Should().Be(5);
    }

    [Fact]
    public async Task GetTrend_ReturnsEmptyBuckets_WhenNoScores()
    {
        var now = DateTimeOffset.UtcNow;
        var trend = await _store.GetTrendAsync("TrendEmptyEval", now.AddHours(-2), now, TimeSpan.FromHours(1));

        trend.EvaluatorName.Should().Be("TrendEmptyEval");
        trend.Buckets.Should().BeEmpty();
    }

    // =========================================================================
    // Category G — GetAgentComparisonAsync
    // =========================================================================

    [Fact]
    public async Task GetAgentComparison_ReturnsAggregatePerAgent()
    {
        const string name = "AgentCompEval";
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "ac-s1", agentName: "agent-x"));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "ac-s2", agentName: "agent-x"));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "ac-s3", agentName: "agent-y"));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "ac-s4", agentName: "agent-y"));
        await _store.WriteScoreAsync(MakeBool(name, sessionId: "ac-s5", agentName: "agent-y"));

        var result = await _store.GetAgentComparisonAsync(name, ["agent-x", "agent-y"]);

        result.Should().ContainKey("agent-x");
        result.Should().ContainKey("agent-y");
        result["agent-x"].Count.Should().Be(2);
        result["agent-y"].Count.Should().Be(3);
    }

    [Fact]
    public async Task GetAgentComparison_ReturnsEmptyDict_WhenNoMatchingScores()
    {
        var result = await _store.GetAgentComparisonAsync("NoSuchEvalXYZ", ["agent-a"]);

        result.Should().BeEmpty();
    }

    // =========================================================================
    // Category H — GetBranchComparisonAsync
    // =========================================================================

    [Fact]
    public async Task GetBranchComparison_FillsBothBranchScores()
    {
        const string sid = "bc-session";
        await _store.WriteScoreAsync(MakeBool("BranchCompEval", sessionId: sid, branchId: "b1", passing: true));
        await _store.WriteScoreAsync(MakeBool("BranchCompEval", sessionId: sid, branchId: "b2", passing: false));

        var result = await _store.GetBranchComparisonAsync(sid, "b1", "b2", ["BranchCompEval"]);

        result.SessionId.Should().Be(sid);
        result.BranchId1.Should().Be("b1");
        result.BranchId2.Should().Be("b2");
        result.Branch1Scores.Should().ContainKey("BranchCompEval");
        result.Branch2Scores.Should().ContainKey("BranchCompEval");
        result.Branch1Scores["BranchCompEval"].Count.Should().Be(1);
        result.Branch2Scores["BranchCompEval"].Count.Should().Be(1);
    }

    // =========================================================================
    // Category I — GetToolUsageSummaryAsync / GetRiskAutonomyDistributionAsync
    //              / GetCostBreakdownAsync
    // =========================================================================

    [Fact]
    public async Task GetToolUsageSummary_ReturnsEmptyDict_WhenNoScores()
    {
        var fresh = new InMemoryScoreStore();

        var result = await fresh.GetToolUsageSummaryAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRiskAutonomyDistribution_ReturnsEmpty_WhenNoMatchingPairs()
    {
        // Only a risk record but no matching autonomy record → no data points
        await _store.WriteScoreAsync(MakeNumeric("TurnRiskEvaluator", sessionId: "ra-s1", score: 7.0));

        var result = await _store.GetRiskAutonomyDistributionAsync();

        // Without a paired TurnAutonomyEvaluator record, no point is produced
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRiskAutonomyDistribution_ReturnsDataPoint_WhenBothScoresPresent()
    {
        const string sid = "ra-paired";
        const string bid = "main";
        await _store.WriteScoreAsync(MakeNumeric("TurnRiskEvaluator", sessionId: sid, branchId: bid, turnIndex: 1, score: 7.0));
        await _store.WriteScoreAsync(MakeNumeric("TurnAutonomyEvaluator", sessionId: sid, branchId: bid, turnIndex: 1, score: 8.0));

        var result = await _store.GetRiskAutonomyDistributionAsync();

        result.Should().HaveCount(1);
        result[0].SessionId.Should().Be(sid);
        result[0].RiskScore.Should().BeApproximately(7.0, 0.001);
        result[0].AutonomyScore.Should().BeApproximately(8.0, 0.001);
    }

    [Fact]
    public async Task GetCostBreakdown_ReturnsEmptyDict_WhenNoJudgeUsage()
    {
        await _store.WriteScoreAsync(MakeBool("CostEval", sessionId: "cost-s1"));
        // JudgeUsage is null → no cost contribution

        var result = await _store.GetCostBreakdownAsync();

        result.Should().NotContainKey("CostEval");
    }
}
