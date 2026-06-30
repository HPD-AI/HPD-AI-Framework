// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using FluentAssertions;
using HPD.Agent.AspNetCore.Serialization;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for the /evals endpoint group.
/// Covers: 503 guard, GET /evals/scores, GET /evals/scores/by-thread,
/// GET /evals/scores/by-version, POST /evals/scores, GET /evals/analytics/evaluators,
/// GET /evals/analytics/trend/{name}, GET /evals/analytics/pass-rate/{name}, GET /evals/analytics/failure-rate/{name},
/// GET /evals/analytics/agent-comparison/{name}, GET /evals/analytics/thread-comparison,
/// GET /evals/analytics/tool-usage, GET /evals/analytics/risk-autonomy, GET /evals/analytics/cost.
/// </summary>
public class EvalEndpointsTests : IClassFixture<EvalTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly EvalTestWebApplicationFactory _factory;

    public EvalEndpointsTests(EvalTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Helper: seed a score into the shared store ───────────────────────────

    private Task SeedAsync(ScoreRecord record) =>
        _factory.ScoreStore.WriteScoreAsync(record).AsTask();

    // =========================================================================
    // Category A — 503 when no IScoreStore registered
    // =========================================================================

    [Fact]
    public async Task GET_evals_scores_Returns503_WhenNoStoreRegistered()
    {
        // Use a separate factory instance that does NOT register IScoreStore
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.GetAsync("/evals/scores?evaluatorName=X");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GET_evals_evaluators_Returns503_WhenNoStoreRegistered()
    {
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.GetAsync("/evals/analytics/evaluators");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GET_old_evals_passRate_Returns404_AfterAnalyticsRoutesWereGrouped()
    {
        var response = await _client.GetAsync("/evals/pass-rate/SomeEval");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_evals_evaluatorCatalog_ReturnsSafetyEvaluators_WithoutScoreStore()
    {
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.GetAsync("/evals/evaluators/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        items.Should().NotBeNull();
        items!.Should().Contain(i =>
            i.GetProperty("name").GetString() == "Prompt Injection" &&
            i.GetProperty("category").GetString() == "Safety");
        items.Should().Contain(i =>
            i.GetProperty("name").GetString() == "Sensitive Data Leak" &&
            i.GetProperty("requiresJudge").GetBoolean());
    }

    [Fact]
    public async Task GET_evals_safetyAnalytics_Returns503_WhenNoStoreRegistered()
    {
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.GetAsync("/evals/analytics/safety");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // =========================================================================
    // Category B — GET /evals/scores
    // =========================================================================

    [Fact]
    public async Task GET_evals_scores_Returns200_WithEmptyArray_WhenNoScores()
    {
        var response = await _client.GetAsync("/evals/scores?evaluatorName=NonExistentEvaluator_Empty");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Trim().Should().Be("[]");
    }

    [Fact]
    public async Task GET_evals_scores_Returns_OnlyMatchingEvaluator()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalFilter_A", sessionId: "sf-s1", threadId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalFilter_A", sessionId: "sf-s2", threadId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalFilter_B", sessionId: "sf-s3", threadId: "main"));

        var response = await _client.GetAsync("/evals/scores?evaluatorName=EvalFilter_A");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().NotBeNull();
        records!.Should().HaveCountGreaterThanOrEqualTo(2);
        records.Should().OnlyContain(r => r.GetProperty("evaluatorName").GetString() == "EvalFilter_A");
    }

    [Fact]
    public async Task GET_evals_scores_Respects_From_DateRange()
    {
        var old = DateTimeOffset.UtcNow.AddHours(-3);
        var recent = DateTimeOffset.UtcNow.AddMinutes(-10);

        await SeedAsync(ScoreRecordFactory.Make("EvalDateRange", sessionId: "dr-s1", createdAt: old));
        await SeedAsync(ScoreRecordFactory.Make("EvalDateRange", sessionId: "dr-s2", createdAt: recent));

        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var response = await _client.GetAsync($"/evals/scores?evaluatorName=EvalDateRange&from={Uri.EscapeDataString(from)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().HaveCount(1);
    }

    [Fact]
    public async Task GET_evals_scores_Returns400_WhenEvaluatorNameMissing()
    {
        var response = await _client.GetAsync("/evals/scores");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category C — GET /evals/scores/by-thread
    // =========================================================================

    [Fact]
    public async Task GET_evals_scores_byThread_Returns_AllThreadsForSession()
    {
        const string sid = "byThread-session-all";
        await SeedAsync(ScoreRecordFactory.Make("EvalBB", sessionId: sid, threadId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalBB", sessionId: sid, threadId: "fork-1"));
        await SeedAsync(ScoreRecordFactory.Make("EvalBB", sessionId: "OTHER-SESSION", threadId: "main"));

        var response = await _client.GetAsync($"/evals/scores/by-thread?sessionId={sid}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().NotBeNull();
        records!.Should().HaveCountGreaterThanOrEqualTo(2);
        records.Should().OnlyContain(r => r.GetProperty("sessionId").GetString() == sid);
    }

    [Fact]
    public async Task GET_evals_scores_byThread_FiltersToSpecificThread()
    {
        const string sid = "byThread-session-specific";
        await SeedAsync(ScoreRecordFactory.Make("EvalBBF", sessionId: sid, threadId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalBBF", sessionId: sid, threadId: "fork-1"));

        var response = await _client.GetAsync($"/evals/scores/by-thread?sessionId={sid}&threadId=main");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().HaveCountGreaterThanOrEqualTo(1);
        records!.Should().OnlyContain(r => r.GetProperty("threadId").GetString() == "main");
    }

    [Fact]
    public async Task GET_evals_scores_byThread_Returns200_WithEmpty_WhenSessionUnknown()
    {
        var response = await _client.GetAsync("/evals/scores/by-thread?sessionId=nonexistent-session-xyz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Trim().Should().Be("[]");
    }

    [Fact]
    public async Task GET_evals_scores_byThread_Returns400_WhenSessionIdMissing()
    {
        var response = await _client.GetAsync("/evals/scores/by-thread");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category D — GET /evals/scores/by-version
    // =========================================================================

    [Fact]
    public async Task GET_evals_scores_byVersion_Returns_VersionMatchedRecords()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalVer", evaluatorVersion: "1.0", sessionId: "ver-s1"));
        await SeedAsync(ScoreRecordFactory.Make("EvalVer", evaluatorVersion: "2.0", sessionId: "ver-s2"));

        var response = await _client.GetAsync("/evals/scores/by-version?evaluatorName=EvalVer&version=1.0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().HaveCountGreaterThanOrEqualTo(1);
        records!.Should().OnlyContain(r => r.GetProperty("evaluatorVersion").GetString() == "1.0");
    }

    [Fact]
    public async Task GET_evals_scores_byVersion_Returns200_WithEmpty_WhenNoMatch()
    {
        var response = await _client.GetAsync("/evals/scores/by-version?evaluatorName=EvalVer&version=99.99");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Trim().Should().Be("[]");
    }

    // =========================================================================
    // Category E — POST /evals/scores
    // =========================================================================

    [Fact]
    public async Task POST_evals_scores_Returns201_WithAssignedId()
    {
        var record = ScoreRecordFactory.Make("PostEval", sessionId: "post-s1");
        // Strip the id — endpoint must assign one
        var body = new
        {
            evaluatorName = record.EvaluatorName,
            evaluatorVersion = record.EvaluatorVersion,
            result = record.Result,
            source = record.Source.ToString(),
            sessionId = record.SessionId,
            threadId = record.ThreadId,
            turnIndex = record.TurnIndex,
            agentName = record.AgentName,
            turnDuration = record.TurnDuration.ToString(),
            samplingRate = record.SamplingRate,
            policy = record.Policy.ToString(),
            createdAt = record.CreatedAt,
        };

        var response = await _client.PostAsJsonAsync("/evals/scores", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync();
        var returned = JsonDocument.Parse(json).RootElement;
        returned.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task POST_evals_scores_AssignsNewId_EvenIfBodyContainsOne()
    {
        var record = ScoreRecordFactory.Make("PostEvalId", sessionId: "post-s2");
        var body = new
        {
            id = "client-provided-id",
            evaluatorName = record.EvaluatorName,
            evaluatorVersion = record.EvaluatorVersion,
            result = record.Result,
            source = record.Source.ToString(),
            sessionId = record.SessionId,
            threadId = record.ThreadId,
            turnIndex = record.TurnIndex,
            agentName = record.AgentName,
            turnDuration = record.TurnDuration.ToString(),
            samplingRate = record.SamplingRate,
            policy = record.Policy.ToString(),
            createdAt = record.CreatedAt,
        };

        var response = await _client.PostAsJsonAsync("/evals/scores", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync();
        var returned = JsonDocument.Parse(json).RootElement;
        returned.GetProperty("id").GetString().Should().NotBe("client-provided-id");
    }

    [Fact]
    public async Task POST_evals_scores_CanBeReadBack_Via_GetScoresByThread()
    {
        const string sid = "post-roundtrip-session";
        const string bid = "post-roundtrip-thread";
        var record = ScoreRecordFactory.Make("PostRoundtrip", sessionId: sid, threadId: bid);
        var body = new
        {
            evaluatorName = record.EvaluatorName,
            evaluatorVersion = record.EvaluatorVersion,
            result = record.Result,
            source = record.Source.ToString(),
            sessionId = sid,
            threadId = bid,
            turnIndex = record.TurnIndex,
            agentName = record.AgentName,
            turnDuration = record.TurnDuration.ToString(),
            samplingRate = record.SamplingRate,
            policy = record.Policy.ToString(),
            createdAt = record.CreatedAt,
        };

        var postResponse = await _client.PostAsJsonAsync("/evals/scores", body);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResponse = await _client.GetAsync($"/evals/scores/by-thread?sessionId={sid}&threadId={bid}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await getResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().NotBeNull();
        records!.Should().HaveCountGreaterThanOrEqualTo(1);
        records.Should().Contain(r =>
            r.GetProperty("evaluatorName").GetString() == "PostRoundtrip" &&
            r.GetProperty("sessionId").GetString() == sid &&
            r.GetProperty("threadId").GetString() == bid);
    }

    [Fact]
    public async Task POST_evals_scores_SetsCreatedAt_WhenNotProvided()
    {
        var record = ScoreRecordFactory.Make("PostCreatedAt", sessionId: "post-s3");
        var body = new
        {
            evaluatorName = record.EvaluatorName,
            evaluatorVersion = record.EvaluatorVersion,
            result = record.Result,
            source = record.Source.ToString(),
            sessionId = record.SessionId,
            threadId = record.ThreadId,
            turnIndex = record.TurnIndex,
            agentName = record.AgentName,
            turnDuration = record.TurnDuration.ToString(),
            samplingRate = record.SamplingRate,
            policy = record.Policy.ToString(),
            // createdAt omitted — endpoint must fill it in
        };

        var response = await _client.PostAsJsonAsync("/evals/scores", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync();
        var returned = JsonDocument.Parse(json).RootElement;
        var createdAtStr = returned.GetProperty("createdAt").GetString();
        createdAtStr.Should().NotBeNullOrWhiteSpace();
        var createdAt = DateTimeOffset.Parse(createdAtStr!);
        createdAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    // =========================================================================
    // Category F — GET /evals/analytics/evaluators
    // =========================================================================

    [Fact]
    public async Task GET_evals_evaluators_Returns_Summary_ForSeededEvaluators()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalSummary_X", sessionId: "summ-s1", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalSummary_X", sessionId: "summ-s2", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalSummary_Y", sessionId: "summ-s3", passing: false));

        var response = await _client.GetAsync("/evals/analytics/evaluators");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaries = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        summaries.Should().NotBeNull();
        var names = summaries!.Select(s => s.GetProperty("evaluatorName").GetString()).ToList();
        names.Should().Contain("EvalSummary_X");
        names.Should().Contain("EvalSummary_Y");
    }

    [Fact]
    public async Task GET_evals_evaluators_Returns200_EmptyArray_WhenNoScores()
    {
        // Use a fresh factory with no seeded scores
        using var fresh = new EvalTestWebApplicationFactory();
        var client = fresh.CreateClient();

        var response = await client.GetAsync("/evals/analytics/evaluators");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Trim().Should().Be("[]");
    }

    // =========================================================================
    // Category G — GET /evals/analytics/trend/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_trend_Returns_ScoreTrendShape()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(ScoreRecordFactory.MakeNumeric("EvalTrend", sessionId: "trend-s1", score: 3.0, createdAt: now.AddMinutes(-90)));
        await SeedAsync(ScoreRecordFactory.MakeNumeric("EvalTrend", sessionId: "trend-s2", score: 7.0, createdAt: now.AddMinutes(-30)));

        var from = now.AddHours(-2).ToString("O");
        var to = now.ToString("O");
        var url = $"/evals/analytics/trend/EvalTrend?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&bucketSize=PT1H";

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;
        doc.GetProperty("evaluatorName").GetString().Should().Be("EvalTrend");
        doc.TryGetProperty("buckets", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GET_evals_trend_Uses_Default_BucketSize_WhenOmitted()
    {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddHours(-2).ToString("O");
        var to = now.ToString("O");
        var url = $"/evals/analytics/trend/EvalTrendDefault?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_evals_trend_Returns400_WhenFromMissing()
    {
        var to = DateTimeOffset.UtcNow.ToString("O");
        var response = await _client.GetAsync($"/evals/analytics/trend/AnyEval?to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category H — GET /evals/analytics/pass-rate/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_passRate_Returns_PassRateObject_WithCorrectShape()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s1", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s2", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s3", passing: false));
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s4", passing: true));

        var response = await _client.GetAsync("/evals/analytics/pass-rate/EvalPR");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;
        doc.GetProperty("evaluatorName").GetString().Should().Be("EvalPR");
        var passRate = doc.GetProperty("passRate").GetDouble();
        passRate.Should().BeInRange(0.0, 1.0);
        passRate.Should().BeApproximately(0.75, 0.01);
    }

    [Fact]
    public async Task GET_evals_passRate_Returns200_WhenNoScores()
    {
        var response = await _client.GetAsync("/evals/analytics/pass-rate/EvalPassRateNoScores_XYZ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("passRate").GetDouble().Should().Be(0.0);
    }

    // =========================================================================
    // Category I — GET /evals/analytics/failure-rate/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_failureRate_Returns_FailureRateObject_WithCorrectShape()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalFR", sessionId: "fr-s1", passing: false));
        await SeedAsync(ScoreRecordFactory.Make("EvalFR", sessionId: "fr-s2", passing: true));

        var response = await _client.GetAsync("/evals/analytics/failure-rate/EvalFR");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("evaluatorName").GetString().Should().Be("EvalFR");
        doc.GetProperty("failureRate").GetDouble().Should().BeInRange(0.0, 1.0);
    }

    // =========================================================================
    // Category J — GET /evals/analytics/agent-comparison/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_agentComparison_Returns_DictionaryByAgentName()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalAC", agentName: "agent-alpha", sessionId: "ac-s1"));
        await SeedAsync(ScoreRecordFactory.Make("EvalAC", agentName: "agent-alpha", sessionId: "ac-s2"));
        await SeedAsync(ScoreRecordFactory.Make("EvalAC", agentName: "agent-beta", sessionId: "ac-s3"));

        var response = await _client.GetAsync("/evals/analytics/agent-comparison/EvalAC?agentNames=agent-alpha,agent-beta");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.TryGetProperty("agent-alpha", out var alpha).Should().BeTrue();
        doc.TryGetProperty("agent-beta", out var beta).Should().BeTrue();
        alpha.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        beta.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GET_evals_agentComparison_Returns400_WhenAgentNamesMissing()
    {
        var response = await _client.GetAsync("/evals/analytics/agent-comparison/EvalAC");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category K — GET /evals/analytics/thread-comparison
    // =========================================================================

    [Fact]
    public async Task GET_evals_threadComparison_Returns_ThreadComparisonResult()
    {
        const string sid = "bc-session";
        await SeedAsync(ScoreRecordFactory.Make("EvalBC", sessionId: sid, threadId: "bc-main", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalBC", sessionId: sid, threadId: "bc-fork", passing: false));

        var url = $"/evals/analytics/thread-comparison?sessionId={sid}&threadId1=bc-main&threadId2=bc-fork&evaluatorNames=EvalBC";
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("sessionId").GetString().Should().Be(sid);
        doc.GetProperty("threadId1").GetString().Should().Be("bc-main");
        doc.GetProperty("threadId2").GetString().Should().Be("bc-fork");
        doc.TryGetProperty("thread1Scores", out _).Should().BeTrue();
        doc.TryGetProperty("thread2Scores", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GET_evals_threadComparison_Returns400_WhenRequiredParamMissing()
    {
        // threadId2 missing
        var response = await _client.GetAsync("/evals/analytics/thread-comparison?sessionId=s1&threadId1=b1&evaluatorNames=E1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category L — GET /evals/analytics/tool-usage
    // =========================================================================

    [Fact]
    public async Task GET_evals_toolUsage_Returns200_WithObjectResult()
    {
        var response = await _client.GetAsync("/evals/analytics/tool-usage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Empty store → empty object {} or populated object — either is valid
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        // Must be parseable JSON
        var act = () => JsonDocument.Parse(body);
        act.Should().NotThrow();
    }

    // =========================================================================
    // Category M — GET /evals/analytics/risk-autonomy
    // =========================================================================

    [Fact]
    public async Task GET_evals_riskAutonomy_Returns200_WithArrayResult()
    {
        var response = await _client.GetAsync("/evals/analytics/risk-autonomy");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // Must be a JSON array
        var doc = JsonDocument.Parse(body).RootElement;
        doc.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // =========================================================================
    // Category N — GET /evals/analytics/cost
    // =========================================================================

    [Fact]
    public async Task GET_evals_cost_Returns200_WithObjectResult()
    {
        var response = await _client.GetAsync("/evals/analytics/cost");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body).RootElement;
        doc.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task GET_evals_analytics_passRate_Returns_SameShape_AsCanonicalAnalyticsRoute()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalGroupedPR", sessionId: "grp-pr-s1", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalGroupedPR", sessionId: "grp-pr-s2", passing: false));

        var response = await _client.GetAsync("/evals/analytics/pass-rate/EvalGroupedPR");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("evaluatorName").GetString().Should().Be("EvalGroupedPR");
        doc.GetProperty("passRate").GetDouble().Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public async Task GET_evals_safetyAnalytics_AggregatesSafetyMetadata()
    {
        var now = DateTimeOffset.UtcNow.AddYears(10);
        await SeedAsync(MakeSafetyScore(
            "Prompt Injection",
            "prompt_injection",
            "none",
            "allow",
            passed: true,
            score: 0.5,
            createdAt: now.AddMinutes(-10)));
        await SeedAsync(MakeSafetyScore(
            "Sensitive Data Leak",
            "sensitive_data_leak",
            "critical",
            "block",
            passed: false,
            score: 6.5,
            createdAt: now.AddMinutes(-5)));
        await SeedAsync(ScoreRecordFactory.MakeNumeric(
            "NotSafety",
            sessionId: "safety-noise",
            score: 10,
            createdAt: now.AddMinutes(-3)));

        var from = Uri.EscapeDataString(now.AddMinutes(-30).ToString("O"));
        var to = Uri.EscapeDataString(now.AddMinutes(30).ToString("O"));
        var response = await _client.GetAsync($"/evals/analytics/safety?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().Be(2);
        body.GetProperty("passedCount").GetInt32().Should().Be(1);
        body.GetProperty("failedCount").GetInt32().Should().Be(1);
        body.GetProperty("passRate").GetDouble().Should().BeApproximately(0.5, 0.0001);
        body.GetProperty("averageSafetyScore").GetDouble().Should().BeApproximately(3.5, 0.0001);
        body.GetProperty("byCategory").GetProperty("prompt_injection").GetInt32().Should().Be(1);
        body.GetProperty("byCategory").GetProperty("sensitive_data_leak").GetInt32().Should().Be(1);
        body.GetProperty("bySeverity").GetProperty("critical").GetInt32().Should().Be(1);
        body.GetProperty("byRecommendedAction").GetProperty("block").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GET_evals_safetyAnalytics_RespectsDateRange()
    {
        var now = DateTimeOffset.UtcNow.AddYears(11);
        await SeedAsync(MakeSafetyScore(
            "Code Security Risk",
            "code_security",
            "high",
            "warn",
            passed: false,
            score: 5.5,
            createdAt: now.AddHours(-3)));
        await SeedAsync(MakeSafetyScore(
            "Content Harm",
            "content_harm",
            "low",
            "allow",
            passed: true,
            score: 1.0,
            createdAt: now.AddMinutes(-5)));

        var from = Uri.EscapeDataString(now.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(now.AddHours(1).ToString("O"));
        var response = await _client.GetAsync($"/evals/analytics/safety?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().Be(1);
        body.GetProperty("byCategory").GetProperty("content_harm").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GET_evals_redTeamCatalog_ReturnsPluginsAndStrategies()
    {
        var pluginsResponse = await _client.GetAsync("/evals/red-team/plugins");
        var strategiesResponse = await _client.GetAsync("/evals/red-team/strategies");

        pluginsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        strategiesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var plugins = await pluginsResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        var strategies = await strategiesResponse.Content.ReadFromJsonAsync<List<JsonElement>>();

        plugins.Should().NotBeNull();
        plugins!.Should().Contain(p =>
            p.GetProperty("id").GetString() == "prompt-injection" &&
            p.GetProperty("category").GetString() == "PromptInjection");
        plugins.Should().Contain(p => p.GetProperty("id").GetString() == "secret-leak");

        strategies.Should().NotBeNull();
        strategies!.Should().Contain(s => s.GetProperty("id").GetString() == "base64");
        strategies.Should().Contain(s => s.GetProperty("id").GetString() == "jailbreak-composite");
    }

    [Fact]
    public async Task GET_evals_redTeamAnalytics_AggregatesAttackSuccess()
    {
        var now = DateTimeOffset.UtcNow.AddYears(12);
        await SeedAsync(MakeRedTeamScore(
            pluginId: "prompt-injection",
            strategyId: "base64",
            attackSucceeded: true,
            createdAt: now.AddMinutes(-10)));
        await SeedAsync(MakeRedTeamScore(
            pluginId: "prompt-injection",
            strategyId: "base64",
            attackSucceeded: false,
            createdAt: now.AddMinutes(-5)));
        await SeedAsync(MakeRedTeamScore(
            pluginId: "secret-leak",
            strategyId: "rot13",
            attackSucceeded: true,
            createdAt: now.AddMinutes(-3)));

        var from = Uri.EscapeDataString(now.AddMinutes(-30).ToString("O"));
        var to = Uri.EscapeDataString(now.AddMinutes(30).ToString("O"));
        var response = await _client.GetAsync($"/evals/analytics/red-team?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("attackSuccessRate").GetDouble().Should().BeApproximately(2.0 / 3.0, 0.0001);
        body.GetProperty("findingCount").GetInt32().Should().Be(2);
        body.GetProperty("attackSuccessRateByPlugin").GetProperty("prompt-injection").GetDouble()
            .Should().BeApproximately(0.5, 0.0001);
        body.GetProperty("attackSuccessRateByPlugin").GetProperty("secret-leak").GetDouble()
            .Should().BeApproximately(1.0, 0.0001);
        body.GetProperty("attackSuccessRateByStrategy").GetProperty("base64").GetDouble()
            .Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public async Task GET_evals_redTeamAnalytics_Findings_ReturnsSucceededAttacks()
    {
        var now = DateTimeOffset.UtcNow.AddYears(13);
        await SeedAsync(MakeRedTeamScore(
            pluginId: "tool-abuse",
            strategyId: "fake-system-message",
            attackGoal: "Call restricted tool",
            attackSucceeded: true,
            createdAt: now.AddMinutes(-1)));
        await SeedAsync(MakeRedTeamScore(
            pluginId: "tool-abuse",
            strategyId: "fake-system-message",
            attackGoal: "Ignored",
            attackSucceeded: false,
            createdAt: now.AddMinutes(-2)));

        var from = Uri.EscapeDataString(now.AddMinutes(-30).ToString("O"));
        var to = Uri.EscapeDataString(now.AddMinutes(30).ToString("O"));
        var response = await _client.GetAsync($"/evals/analytics/red-team/findings?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var findings = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        findings.Should().NotBeNull();
        findings!.Should().ContainSingle();
        findings[0].GetProperty("pluginId").GetString().Should().Be("tool-abuse");
        findings[0].GetProperty("strategyId").GetString().Should().Be("fake-system-message");
        findings[0].GetProperty("attackGoal").GetString().Should().Be("Call restricted tool");
    }

    [Fact]
    public async Task GET_evals_redTeamAnalytics_Returns503_WhenNoStoreRegistered()
    {
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.GetAsync("/evals/analytics/red-team");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // =========================================================================
    // Category O — GET /evals/runs
    // =========================================================================

    [Fact]
    public async Task GET_evals_runs_Returns_FilteredRunRecords()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-runs-a", "scenario-a", "iter-1"));
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-runs-b", "scenario-b", "iter-1"));

        var response = await _client.GetAsync("/evals/runs?executionName=exec-runs-a");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.ValueKind.Should().Be(JsonValueKind.Array);
        doc.GetArrayLength().Should().Be(1);
        doc[0].GetProperty("executionName").GetString().Should().Be("exec-runs-a");
        doc[0].GetProperty("scenarioName").GetString().Should().Be("scenario-a");
    }

    [Fact]
    public async Task GET_evals_runs_Returns_AllRunRecords_WhenNoFilters()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-runs-all-a", "scenario-a", "iter-1"));
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-runs-all-b", "scenario-b", "iter-1"));

        var response = await _client.GetAsync("/evals/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray()
            .Select(e => e.GetProperty("executionName").GetString())
            .Should()
            .Contain(["exec-runs-all-a", "exec-runs-all-b"]);
    }

    [Fact]
    public async Task GET_evals_runs_Returns503_WhenNoScoreStoreRegistered()
    {
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.GetAsync("/evals/runs");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GET_evals_runs_executions_Returns_LatestExecutionNames()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-list-a", "scenario-a", "iter-1", DateTimeOffset.UtcNow.AddMinutes(-5)));
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-list-b", "scenario-b", "iter-1", DateTimeOffset.UtcNow));

        var response = await _client.GetAsync("/evals/runs/executions?count=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var names = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        names.Should().Contain(["exec-list-a", "exec-list-b"]);
    }

    [Fact]
    public async Task GET_evals_runs_scenarios_Returns_ScenarioNames()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-scenarios", "scenario-x", "iter-1"));
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-scenarios", "scenario-y", "iter-1"));

        var response = await _client.GetAsync("/evals/runs/scenarios?executionName=exec-scenarios");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var names = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        names.Should().Contain(["scenario-x", "scenario-y"]);
    }

    [Fact]
    public async Task GET_evals_runs_iterations_Returns_IterationNames()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-iterations", "scenario-i", "iter-a"));
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-iterations", "scenario-i", "iter-b"));

        var response = await _client.GetAsync("/evals/runs/iterations?executionName=exec-iterations&scenarioName=scenario-i");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var names = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        names.Should().Contain(["iter-a", "iter-b"]);
    }

    [Fact]
    public async Task DELETE_evals_runs_RemovesMatchingRuns()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-delete", "scenario-d", "iter-1"));

        var delete = await _client.DeleteAsync("/evals/runs?executionName=exec-delete");
        var get = await _client.GetAsync("/evals/runs?executionName=exec-delete");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DELETE_evals_runs_ByScenario_OnlyRemovesMatchingScenario()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-delete-scenario", "scenario-keep", "iter-1"));
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-delete-scenario", "scenario-delete", "iter-1"));

        var delete = await _client.DeleteAsync("/evals/runs?scenarioName=scenario-delete");
        var get = await _client.GetAsync("/evals/runs?executionName=exec-delete-scenario");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var scenarios = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray()
            .Select(e => e.GetProperty("scenarioName").GetString())
            .ToList();
        scenarios.Should().Contain("scenario-keep");
        scenarios.Should().NotContain("scenario-delete");
    }

    [Fact]
    public async Task DELETE_evals_runs_ByIteration_OnlyRemovesMatchingIteration()
    {
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-delete-iteration", "scenario-i", "iter-keep"));
        await _factory.ScoreStore.WriteRunAsync(MakeRun("exec-delete-iteration", "scenario-i", "iter-delete"));

        var delete = await _client.DeleteAsync("/evals/runs?iterationName=iter-delete");
        var get = await _client.GetAsync("/evals/runs?executionName=exec-delete-iteration&scenarioName=scenario-i");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var iterations = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray()
            .Select(e => e.GetProperty("iterationName").GetString())
            .ToList();
        iterations.Should().Contain("iter-keep");
        iterations.Should().NotContain("iter-delete");
    }

    // =========================================================================
    // Category P — /evals/datasets
    // =========================================================================

    [Fact]
    public async Task POST_evals_datasets_RegistersStringDatasetVersion()
    {
        var body = new
        {
            datasetId = "asp-dataset-post",
            version = "v1",
            description = "ASP.NET dataset endpoint test",
            registeredAt = DateTimeOffset.UtcNow,
            cases = new[]
            {
                new { caseId = "case-a", name = "Case A", version = "v1", input = "hello", groundTruth = "world" }
            }
        };

        var response = await _client.PostAsJsonAsync("/evals/datasets", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("datasetId").GetString().Should().Be("asp-dataset-post");
        doc.GetProperty("version").GetString().Should().Be("v1");
        doc.GetProperty("caseCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GET_evals_datasets_Returns503_WhenNoDatasetStoreRegistered()
    {
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.GetAsync("/evals/datasets");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task POST_evals_datasets_Returns503_WhenNoDatasetStoreRegistered()
    {
        using var noStoreFactory = new TestWebApplicationFactory();
        var client = noStoreFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/evals/datasets", new
        {
            datasetId = "missing-store",
            version = "v1",
            cases = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task POST_evals_datasets_ReturnsValidationProblem_WhenDatasetIdMissing()
    {
        var response = await _client.PostAsJsonAsync("/evals/datasets", new
        {
            version = "v1",
            cases = new[] { new { caseId = "case-a", input = "hello" } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("errors").TryGetProperty("RegisterDatasetError", out _).Should().BeTrue();
    }

    [Fact]
    public async Task POST_evals_datasets_ReturnsValidationProblem_WhenVersionMissing()
    {
        var response = await _client.PostAsJsonAsync("/evals/datasets", new
        {
            datasetId = "missing-version",
            cases = new[] { new { caseId = "case-a", input = "hello" } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("errors").TryGetProperty("RegisterDatasetError", out _).Should().BeTrue();
    }

    [Fact]
    public async Task POST_evals_datasets_DuplicateSameContent_IsIdempotent()
    {
        var registeredAt = DateTimeOffset.UtcNow;
        var body = new
        {
            datasetId = "asp-dataset-idempotent",
            version = "v1",
            registeredAt,
            cases = new[]
            {
                new { caseId = "case-a", input = "same", groundTruth = "truth" }
            }
        };

        var first = await _client.PostAsJsonAsync("/evals/datasets", body);
        var second = await _client.PostAsJsonAsync("/evals/datasets", body);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_evals_datasets_DuplicateVersionDifferentContent_ReturnsValidationProblem()
    {
        var registeredAt = DateTimeOffset.UtcNow;
        var first = new
        {
            datasetId = "asp-dataset-conflict",
            version = "v1",
            registeredAt,
            cases = new[] { new { caseId = "case-a", input = "first", groundTruth = "truth" } }
        };
        var second = new
        {
            datasetId = "asp-dataset-conflict",
            version = "v1",
            registeredAt,
            cases = new[] { new { caseId = "case-a", input = "second", groundTruth = "truth" } }
        };

        var firstResponse = await _client.PostAsJsonAsync("/evals/datasets", first);
        var secondResponse = await _client.PostAsJsonAsync("/evals/datasets", second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("errors").TryGetProperty("RegisterDatasetError", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GET_evals_datasets_Returns_RegisteredDatasetMetadata()
    {
        await RegisterDatasetVersionAsync("asp-dataset-list", "v1", ("case-a", "hello", "world"));

        var response = await _client.GetAsync("/evals/datasets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var datasets = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        datasets.EnumerateArray()
            .Should()
            .Contain(e => e.GetProperty("datasetId").GetString() == "asp-dataset-list");
    }

    [Fact]
    public async Task GET_evals_datasets_byId_Returns404_WhenUnknown()
    {
        var response = await _client.GetAsync("/evals/datasets/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_evals_dataset_versions_Returns_AllVersions()
    {
        await RegisterDatasetVersionAsync("asp-dataset-versions", "v1", ("case-a", "hello", "world"));
        await RegisterDatasetVersionAsync("asp-dataset-versions", "v2", ("case-a", "hello again", "world"));

        var response = await _client.GetAsync("/evals/datasets/asp-dataset-versions/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray()
            .Select(e => e.GetProperty("version").GetString())
            .ToList();
        versions.Should().Contain(["v1", "v2"]);
    }

    [Fact]
    public async Task GET_evals_dataset_version_Returns_StringCases()
    {
        await RegisterDatasetVersionAsync("asp-dataset-version", "v1", ("case-a", "hello", "world"));

        var response = await _client.GetAsync("/evals/datasets/asp-dataset-version/versions/v1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("datasetId").GetString().Should().Be("asp-dataset-version");
        doc.GetProperty("cases").GetArrayLength().Should().Be(1);
        doc.GetProperty("cases")[0].GetProperty("input").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task GET_evals_dataset_activeCases_Returns_CasesActiveAtTime()
    {
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);
        var t2 = DateTimeOffset.UtcNow.AddHours(-1);
        await RegisterDatasetVersionAsync("asp-dataset-active", "v1", [("case-a", "old", "truth")], t1);
        await RegisterDatasetVersionAsync("asp-dataset-active", "v2", [("case-a", "new", "truth")], t2);

        var at = Uri.EscapeDataString(t1.AddMinutes(10).ToString("O"));
        var response = await _client.GetAsync($"/evals/datasets/asp-dataset-active/active-cases?at={at}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cases = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        cases.GetArrayLength().Should().Be(1);
        cases[0].GetProperty("input").GetString().Should().Be("old");
    }

    [Fact]
    public async Task GET_evals_dataset_caseHistory_Returns_Scd2History()
    {
        await RegisterDatasetVersionAsync("asp-dataset-history", "v1", [("case-a", "old", "truth")], DateTimeOffset.UtcNow.AddHours(-2));
        await RegisterDatasetVersionAsync("asp-dataset-history", "v2", [("case-a", "new", "truth")], DateTimeOffset.UtcNow.AddHours(-1));

        var response = await _client.GetAsync("/evals/datasets/asp-dataset-history/cases/case-a/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        history.GetArrayLength().Should().Be(2);
        history[0].TryGetProperty("validTo", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GET_evals_dataset_diff_Returns_AddedRemovedChangedCases()
    {
        await RegisterDatasetVersionAsync("asp-dataset-diff", "v1",
            ("case-a", "same", "truth"),
            ("case-b", "remove me", "truth"));
        await RegisterDatasetVersionAsync("asp-dataset-diff", "v2",
            ("case-a", "changed", "truth"),
            ("case-c", "add me", "truth"));

        var response = await _client.GetAsync("/evals/datasets/asp-dataset-diff/diff?fromVersion=v1&toVersion=v2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var diff = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        diff.GetProperty("added").GetArrayLength().Should().Be(1);
        diff.GetProperty("removed").GetArrayLength().Should().Be(1);
        diff.GetProperty("changed").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void EvalEndpointJsonContext_Serializes_NewDatasetDtos_WithoutReflectionFallback()
    {
        var request = new RegisterStringDatasetRequest
        {
            DatasetId = "json-context-dataset",
            Version = "v1",
            Cases =
            [
                new StringEvalCaseDto
                {
                    CaseId = "case-a",
                    Input = "hello",
                    GroundTruth = "world",
                }
            ],
        };

        var json = JsonSerializer.Serialize(
            request,
            HPDAgentAspNetCoreJsonSerializerContext.Default.RegisterStringDatasetRequest);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            HPDAgentAspNetCoreJsonSerializerContext.Default.RegisterStringDatasetRequest);

        roundTrip.Should().NotBeNull();
        roundTrip!.DatasetId.Should().Be("json-context-dataset");
        roundTrip.Cases.Should().HaveCount(1);
    }

    private static EvaluationRunRecord MakeRun(
        string executionName,
        string scenarioName,
        string iterationName,
        DateTimeOffset? createdAt = null)
    {
        var metric = new BooleanMetric("Pass") { Value = true };
        return new EvaluationRunRecord
        {
            Id = Guid.NewGuid().ToString(),
            ExecutionName = executionName,
            ScenarioName = scenarioName,
            IterationName = iterationName,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Messages = [new ChatMessage(ChatRole.User, "input")],
            ModelResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "output")]),
            EvaluationResult = new EvaluationResult([metric]),
            Source = EvaluationSource.Test,
            AgentName = "test-agent",
            SessionId = executionName,
            ThreadId = scenarioName,
            TurnIndex = 0,
            TaskDuration = TimeSpan.FromMilliseconds(10),
            EvaluatorDuration = TimeSpan.FromMilliseconds(5),
            TotalDuration = TimeSpan.FromMilliseconds(15),
        };
    }

    private static ScoreRecord MakeSafetyScore(
        string evaluatorName,
        string category,
        string severity,
        string action,
        bool passed,
        double score,
        DateTimeOffset createdAt)
    {
        var scoreMetric = new NumericMetric(evaluatorName)
        {
            Value = score,
            Interpretation = new EvaluationMetricInterpretation(
                passed ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: !passed),
        };
        scoreMetric.AddOrUpdateMetadata("safety-category", category);
        scoreMetric.AddOrUpdateMetadata("safety-severity", severity);
        scoreMetric.AddOrUpdateMetadata("safety-recommended-action", action);
        scoreMetric.AddOrUpdateMetadata("safety-passed", passed.ToString());

        var passedMetric = new BooleanMetric($"{evaluatorName} Passed")
        {
            Value = passed,
            Interpretation = new EvaluationMetricInterpretation(
                passed ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: !passed),
        };
        passedMetric.AddOrUpdateMetadata("safety-category", category);
        passedMetric.AddOrUpdateMetadata("safety-severity", severity);
        passedMetric.AddOrUpdateMetadata("safety-recommended-action", action);
        passedMetric.AddOrUpdateMetadata("safety-passed", passed.ToString());

        return new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = "1.0",
            Result = new EvaluationResult([scoreMetric, passedMetric]),
            Source = EvaluationSource.Test,
            SessionId = $"safety-{Guid.NewGuid():N}",
            ThreadId = "main",
            TurnIndex = 0,
            AgentName = "safety-agent",
            TurnDuration = TimeSpan.FromMilliseconds(50),
            SamplingRate = 1.0,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = createdAt,
        };
    }

    private static ScoreRecord MakeRedTeamScore(
        string pluginId,
        string strategyId,
        bool attackSucceeded,
        DateTimeOffset createdAt,
        string attackGoal = "Bypass policy")
    {
        var metric = new BooleanMetric("Red Team Passed")
        {
            Value = !attackSucceeded,
            Interpretation = new EvaluationMetricInterpretation(
                attackSucceeded ? EvaluationRating.Unacceptable : EvaluationRating.Good,
                failed: attackSucceeded),
        };

        return new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = "Red Team Passed",
            EvaluatorVersion = "1.0",
            Result = new EvaluationResult([metric]),
            Source = EvaluationSource.Test,
            SessionId = $"redteam-{Guid.NewGuid():N}",
            ThreadId = "main",
            TurnIndex = 0,
            AgentName = "redteam-agent",
            TurnDuration = TimeSpan.FromMilliseconds(50),
            SamplingRate = 1.0,
            Policy = EvalPolicy.MustAlwaysPass,
            CreatedAt = createdAt,
            RedTeamPluginId = pluginId,
            RedTeamStrategyId = strategyId,
            RedTeamCategory = "PromptInjection",
            RedTeamSeverity = "High",
            AttackGoal = attackGoal,
            AttackSucceeded = attackSucceeded,
        };
    }

    private Task RegisterDatasetVersionAsync(
        string datasetId,
        string version,
        params (string CaseId, string Input, string GroundTruth)[] cases)
        => RegisterDatasetVersionAsync(datasetId, version, cases, null);

    private async Task RegisterDatasetVersionAsync(
        string datasetId,
        string version,
        (string CaseId, string Input, string GroundTruth)[] cases,
        DateTimeOffset? registeredAt)
    {
        await _factory.DatasetStore.RegisterDatasetVersionAsync(
            new HPD.Agent.Evaluations.Batch.Dataset<string>
            {
                DatasetId = datasetId,
                Version = version,
                Cases = cases.Select(c => new HPD.Agent.Evaluations.Batch.EvalCase<string>
                {
                    CaseId = c.CaseId,
                    Version = version,
                    Input = c.Input,
                    GroundTruth = c.GroundTruth,
                }).ToList(),
            },
            new DatasetRegistrationOptions<string>
            {
                RegisteredAt = registeredAt,
            });
    }
}
