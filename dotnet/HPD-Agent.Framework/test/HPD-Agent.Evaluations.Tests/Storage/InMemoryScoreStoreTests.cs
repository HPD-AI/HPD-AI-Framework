// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
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
        string threadId = "thread-1",
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
            ThreadId = threadId,
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
        string threadId = "thread-1",
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
            ThreadId = threadId,
            TurnIndex = turnIndex,
            AgentName = agentName,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

    private static EvaluationRunRecord MakeRunRecord(
        string executionName = "exec-1",
        string scenarioName = "scenario-1",
        string iterationName = "1",
        DateTimeOffset? createdAt = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ExecutionName = executionName,
            ScenarioName = scenarioName,
            IterationName = iterationName,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Messages = [new ChatMessage(ChatRole.User, "hello")],
            ModelResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]),
            EvaluationResult = new EvaluationResult(new BooleanMetric("Test") { Value = true }),
            Tags = ["unit-test"],
            Source = EvaluationSource.Test,
            AgentName = "TestAgent",
            SessionId = executionName,
            ThreadId = scenarioName,
            TurnIndex = 0,
        };

    private static ScoreRecord MakeRedTeamRecord(
        bool attackSucceeded,
        string pluginId = "prompt-injection",
        string strategyId = "basic",
        string category = "PromptInjection",
        string severity = "High",
        string attackGoal = "Reveal hidden instructions",
        DateTimeOffset? createdAt = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = "PromptInjectionEvaluator",
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new BooleanMetric("Prompt Injection Safe") { Value = !attackSucceeded }),
            Source = EvaluationSource.Test,
            SessionId = "sess-1",
            ThreadId = "thread-1",
            TurnIndex = 0,
            AgentName = "TestAgent",
            Policy = EvalPolicy.MustAlwaysPass,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            RedTeamPluginId = pluginId,
            RedTeamStrategyId = strategyId,
            RedTeamCategory = category,
            RedTeamSeverity = severity,
            AttackGoal = attackGoal,
            AttackSucceeded = attackSucceeded,
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
    public async Task Write_ThenGetBySession_PreservesDatasetProvenance()
    {
        var store = new InMemoryScoreStore();
        var validFrom = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var validTo = DateTimeOffset.Parse("2026-02-01T00:00:00Z");
        var record = new ScoreRecord
        {
            Id = "score-1",
            EvaluatorName = "E",
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new BooleanMetric("Test") { Value = true }),
            Source = EvaluationSource.Test,
            SessionId = "sess-abc",
            ThreadId = "case-1",
            TurnIndex = 0,
            AgentName = "TestAgent",
            DatasetId = "support-bench",
            DatasetVersion = "2026.02",
            CaseId = "case-001",
            CaseVersion = "2",
            CaseValidFrom = validFrom,
            CaseValidTo = validTo,
            Policy = EvalPolicy.MustAlwaysPass,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await store.WriteScoreAsync(record);

        var results = await store.GetScoresAsync(sessionId: "sess-abc").ToListAsync();
        var roundTripped = results.Should().ContainSingle().Which;
        roundTripped.DatasetId.Should().Be("support-bench");
        roundTripped.DatasetVersion.Should().Be("2026.02");
        roundTripped.CaseId.Should().Be("case-001");
        roundTripped.CaseVersion.Should().Be("2");
        roundTripped.CaseValidFrom.Should().Be(validFrom);
        roundTripped.CaseValidTo.Should().Be(validTo);
    }

    [Fact]
    public async Task Write_ThenGetBySession_PreservesMultipleCaseVersions()
    {
        var store = new InMemoryScoreStore();
        var v1 = WithDatasetVersion("1", validTo: DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        var v2 = WithDatasetVersion("2", validTo: null);

        await store.WriteScoreAsync(v1);
        await store.WriteScoreAsync(v2);

        var results = await store.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        results.Should().HaveCount(2);
        results.Select(r => r.CaseId).Should().OnlyContain(id => id == "case-001");
        results.Select(r => r.CaseVersion).Should().BeEquivalentTo(["1", "2"]);
        results.Single(r => r.CaseVersion == "1").CaseValidTo.Should()
            .Be(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        results.Single(r => r.CaseVersion == "2").CaseValidTo.Should().BeNull();

        static ScoreRecord WithDatasetVersion(string version, DateTimeOffset? validTo) => new()
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = "E",
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new BooleanMetric("Test") { Value = true }),
            Source = EvaluationSource.Test,
            SessionId = "sess-1",
            ThreadId = $"case-v{version}",
            TurnIndex = 0,
            AgentName = "TestAgent",
            DatasetId = "support-bench",
            DatasetVersion = "2026.02",
            CaseId = "case-001",
            CaseVersion = version,
            CaseValidFrom = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            CaseValidTo = validTo,
            Policy = EvalPolicy.MustAlwaysPass,
            CreatedAt = DateTimeOffset.UtcNow,
        };
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
    public async Task GetBySession_FiltersByThreadId()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("E", true, sessionId: "s1", threadId: "b1"));
        await store.WriteScoreAsync(MakeBoolRecord("E", false, sessionId: "s1", threadId: "b2"));

        var results = await store.GetScoresAsync("s1", threadId: "b1").ToListAsync();
        results.Should().ContainSingle().Which.ThreadId.Should().Be("b1");
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

    // ── GetThreadComparisonAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetThreadComparison_TwoThreads_ReturnsPerThreadScores()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeNumericRecord("Quality", 0.9, sessionId: "s1", threadId: "b1"));
        await store.WriteScoreAsync(MakeNumericRecord("Quality", 0.6, sessionId: "s1", threadId: "b2"));

        var comparison = await store.GetThreadComparisonAsync(
            "s1", "b1", "b2", ["Quality"]);

        comparison.SessionId.Should().Be("s1");
        comparison.Thread1Scores["Quality"].Average.Should().BeApproximately(0.9, 0.01);
        comparison.Thread2Scores["Quality"].Average.Should().BeApproximately(0.6, 0.01);
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
            ThreadId = "thread-1",
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
            ThreadId = "thread-1",
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

    // ── Red-team analytics ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAttackSuccessRate_NoRedTeamScores_ReturnsZero()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeBoolRecord("RegularEvaluator", passed: false));

        var rate = await store.GetAttackSuccessRateAsync();

        rate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetAttackSuccessRate_HalfSucceeded_ReturnsHalf()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: true));
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: false));
        await store.WriteScoreAsync(MakeBoolRecord("RegularEvaluator", passed: false));

        var rate = await store.GetAttackSuccessRateAsync();

        rate.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public async Task GetAttackSuccessRate_DateRange_FiltersRedTeamScores()
    {
        var store = new InMemoryScoreStore();
        var now = DateTimeOffset.UtcNow;
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: true, createdAt: now.AddDays(-2)));
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: true, createdAt: now.AddHours(-1)));
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: false, createdAt: now.AddMinutes(-30)));

        var rate = await store.GetAttackSuccessRateAsync(
            from: now.AddHours(-2),
            to: now);

        rate.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public async Task GetAttackSuccessRateByPlugin_GroupsByPlugin()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: true, pluginId: "prompt-injection"));
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: false, pluginId: "prompt-injection"));
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: true, pluginId: "data-leakage"));

        var rates = await store.GetAttackSuccessRateByPluginAsync();

        rates["prompt-injection"].Should().BeApproximately(0.5, 0.001);
        rates["data-leakage"].Should().Be(1.0);
    }

    [Fact]
    public async Task GetAttackSuccessRateByStrategy_GroupsByStrategy()
    {
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: true, strategyId: "base64"));
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: true, strategyId: "base64"));
        await store.WriteScoreAsync(MakeRedTeamRecord(attackSucceeded: false, strategyId: "roleplay"));

        var rates = await store.GetAttackSuccessRateByStrategyAsync();

        rates["base64"].Should().Be(1.0);
        rates["roleplay"].Should().Be(0.0);
    }

    [Fact]
    public async Task GetRedTeamFindings_ReturnsOnlySucceededAttacksNewestFirst()
    {
        var store = new InMemoryScoreStore();
        var now = DateTimeOffset.UtcNow;
        await store.WriteScoreAsync(MakeRedTeamRecord(
            attackSucceeded: true,
            pluginId: "data-leakage",
            strategyId: "markdown-authority",
            category: "DataLeakage",
            severity: "Critical",
            attackGoal: "Extract secrets",
            createdAt: now.AddMinutes(-1)));
        await store.WriteScoreAsync(MakeRedTeamRecord(
            attackSucceeded: false,
            pluginId: "data-leakage",
            createdAt: now));
        await store.WriteScoreAsync(MakeRedTeamRecord(
            attackSucceeded: true,
            pluginId: "tool-abuse",
            strategyId: "fake-system-message",
            category: "ToolAbuse",
            severity: "Medium",
            attackGoal: "Call restricted tool",
            createdAt: now.AddMinutes(1)));

        var findings = await store.GetRedTeamFindingsAsync();

        findings.Should().HaveCount(2);
        findings[0].PluginId.Should().Be("tool-abuse");
        findings[0].StrategyId.Should().Be("fake-system-message");
        findings[0].Category.Should().Be("ToolAbuse");
        findings[0].AttackGoal.Should().Be("Call restricted tool");
        findings[1].PluginId.Should().Be("data-leakage");
        findings[1].Severity.Should().Be("Critical");
    }

    // ── EvaluationRunRecord / IEvaluationResultStore methods ─────────────────

    [Fact]
    public async Task WriteRun_ThenGetRuns_RoundTripsFullPayload()
    {
        var store = new InMemoryScoreStore();
        var run = MakeRunRecord(
            executionName: "nightly",
            scenarioName: "case-1",
            iterationName: "2");

        await store.WriteRunAsync(run);

        var results = await store.GetRunsAsync("nightly", "case-1", "2").ToListAsync();
        var roundTripped = results.Should().ContainSingle().Which;
        roundTripped.Id.Should().Be(run.Id);
        roundTripped.Messages.Should().ContainSingle();
        roundTripped.ModelResponse.Text.Should().Be("response");
        roundTripped.EvaluationResult.Metrics.Should().ContainKey("Test");
        roundTripped.Tags.Should().Contain("unit-test");
    }

    [Fact]
    public async Task GetRuns_FiltersExecutionScenarioAndIteration()
    {
        var store = new InMemoryScoreStore();
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "2"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-b", "1"));
        await store.WriteRunAsync(MakeRunRecord("other", "case-a", "1"));

        var results = await store.GetRunsAsync("exec", "case-a", "2").ToListAsync();

        results.Should().ContainSingle();
        results[0].ExecutionName.Should().Be("exec");
        results[0].ScenarioName.Should().Be("case-a");
        results[0].IterationName.Should().Be("2");
    }

    [Fact]
    public async Task DeleteRuns_RemovesMatchingRuns()
    {
        var store = new InMemoryScoreStore();
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "2"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-b", "1"));

        await store.DeleteRunsAsync("exec", "case-a", "1");

        var remaining = await store.GetRunsAsync("exec").ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(r => r.ScenarioName == "case-a" && r.IterationName == "1");
    }

    [Fact]
    public async Task GetLatestExecutionNames_ReturnsMostRecentRunExecutions()
    {
        var store = new InMemoryScoreStore();
        var now = DateTimeOffset.UtcNow;
        await store.WriteRunAsync(MakeRunRecord("old", createdAt: now.AddDays(-1)));
        await store.WriteRunAsync(MakeRunRecord("new", createdAt: now));
        await store.WriteRunAsync(MakeRunRecord("old", "case-2", "1", now.AddHours(-1)));

        var names = await store.GetLatestExecutionNamesAsync(maxCount: 1).ToListAsync();
        names.Should().Equal("new");
    }

    [Fact]
    public async Task GetScenarioNames_ReturnsScenarioNamesForExecution()
    {
        var store = new InMemoryScoreStore();
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-b", "1"));
        await store.WriteRunAsync(MakeRunRecord("other", "case-c", "1"));

        var scenarios = await store.GetScenarioNamesAsync("exec").ToListAsync();
        scenarios.Should().HaveCount(2)
            .And.Contain("case-a")
            .And.Contain("case-b");
    }

    [Fact]
    public async Task GetIterationNames_ReturnsRunIterationNames()
    {
        var store = new InMemoryScoreStore();
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "2"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-b", "1"));

        var iterations = await store.GetIterationNamesAsync("exec", "case-a").ToListAsync();
        iterations.Should().Equal("1", "2");
    }

    [Fact]
    public async Task MsWriteResults_ThenReadResults_RoundTripsScenarioRunResult()
    {
        var store = new InMemoryScoreStore();
        var scenario = new ScenarioRunResult(
            scenarioName: "case-1",
            iterationName: "1",
            executionName: "exec",
            creationTime: DateTime.UtcNow,
            messages: [new ChatMessage(ChatRole.User, "hello")],
            modelResponse: new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]),
            evaluationResult: new EvaluationResult(new BooleanMetric("Test") { Value = true }),
            tags: ["ms-compat"]);

        await store.WriteResultsAsync([scenario]);

        var results = await store.ReadResultsAsync("exec", "case-1", "1").ToListAsync();
        var roundTripped = results.Should().ContainSingle().Which;
        roundTripped.ExecutionName.Should().Be("exec");
        roundTripped.ScenarioName.Should().Be("case-1");
        roundTripped.IterationName.Should().Be("1");
        roundTripped.ModelResponse.Text.Should().Be("response");
        roundTripped.Tags.Should().Contain("ms-compat");
    }

    [Fact]
    public async Task MsReadResults_NullFilters_ReturnAllScenarioRunResults()
    {
        var store = new InMemoryScoreStore();
        await store.WriteRunAsync(MakeRunRecord("exec-a", "case-1", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec-a", "case-2", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec-b", "case-1", "1"));

        var results = await store.ReadResultsAsync(null, null, null).ToListAsync();

        results.Should().HaveCount(3);
        results.Select(r => r.ExecutionName).Distinct().Should().BeEquivalentTo(["exec-a", "exec-b"]);
    }

    [Fact]
    public async Task MsDeleteResults_RemovesOnlyMatchingScenarioRunResults()
    {
        var store = new InMemoryScoreStore();
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "2"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-b", "1"));
        await store.WriteRunAsync(MakeRunRecord("other", "case-a", "1"));

        await store.DeleteResultsAsync("exec", "case-a", null);

        var remaining = await store.ReadResultsAsync(null, null, null).ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().Contain(r => r.ExecutionName == "exec" && r.ScenarioName == "case-b");
        remaining.Should().Contain(r => r.ExecutionName == "other" && r.ScenarioName == "case-a");
        remaining.Should().NotContain(r => r.ExecutionName == "exec" && r.ScenarioName == "case-a");
    }

    [Fact]
    public async Task MsGetScenarioAndIterationNames_NullFilters_ReturnDistinctSortedNames()
    {
        var store = new InMemoryScoreStore();
        await store.WriteRunAsync(MakeRunRecord("exec-b", "case-b", "2"));
        await store.WriteRunAsync(MakeRunRecord("exec-a", "case-a", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec-a", "case-a", "1"));

        var scenarios = await store.GetScenarioNamesAsync(null).ToListAsync();
        var iterations = await store.GetIterationNamesAsync(null, null).ToListAsync();

        scenarios.Should().Equal("case-a", "case-b");
        iterations.Should().Equal("1", "2");
    }

    [Fact]
    public async Task MsWriteResults_PreservesCreationTimeMessagesMetricsAndTags()
    {
        var store = new InMemoryScoreStore();
        var created = new DateTime(2026, 2, 20, 12, 34, 56, DateTimeKind.Utc);
        var scenario = new ScenarioRunResult(
            scenarioName: "case-1",
            iterationName: "7",
            executionName: "exec",
            creationTime: created,
            messages:
            [
                new ChatMessage(ChatRole.System, "system"),
                new ChatMessage(ChatRole.User, "hello"),
            ],
            modelResponse: new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]),
            evaluationResult: new EvaluationResult(
                new BooleanMetric("Pass") { Value = true },
                new NumericMetric("Quality") { Value = 0.75 }),
            tags: ["nightly", "smoke"]);

        await store.WriteResultsAsync([scenario]);

        var run = (await store.GetRunsAsync("exec", "case-1", "7").ToListAsync())
            .Should().ContainSingle().Which;
        run.CreatedAt.UtcDateTime.Should().Be(created);
        run.Messages.Should().HaveCount(2);
        run.EvaluationResult.Metrics.Should().ContainKeys("Pass", "Quality");
        run.Tags.Should().Equal("nightly", "smoke");

        var exported = (await store.ReadResultsAsync("exec", "case-1", "7").ToListAsync())
            .Should().ContainSingle().Which;
        exported.CreationTime.Should().Be(created);
        exported.Messages.Should().HaveCount(2);
        exported.EvaluationResult.Metrics.Should().ContainKeys("Pass", "Quality");
        exported.Tags.Should().Equal("nightly", "smoke");
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
