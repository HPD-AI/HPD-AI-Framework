// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for the /evals endpoint group.
/// Covers: 503 guard, GET /evals/scores, GET /evals/scores/by-branch,
/// GET /evals/scores/by-version, POST /evals/scores, GET /evals/evaluators,
/// GET /evals/trend/{name}, GET /evals/pass-rate/{name}, GET /evals/failure-rate/{name},
/// GET /evals/agent-comparison/{name}, GET /evals/branch-comparison,
/// GET /evals/tool-usage, GET /evals/risk-autonomy, GET /evals/cost.
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

        var response = await client.GetAsync("/evals/evaluators");

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
        await SeedAsync(ScoreRecordFactory.Make("EvalFilter_A", sessionId: "sf-s1", branchId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalFilter_A", sessionId: "sf-s2", branchId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalFilter_B", sessionId: "sf-s3", branchId: "main"));

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
    // Category C — GET /evals/scores/by-branch
    // =========================================================================

    [Fact]
    public async Task GET_evals_scores_byBranch_Returns_AllBranchesForSession()
    {
        const string sid = "byBranch-session-all";
        await SeedAsync(ScoreRecordFactory.Make("EvalBB", sessionId: sid, branchId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalBB", sessionId: sid, branchId: "fork-1"));
        await SeedAsync(ScoreRecordFactory.Make("EvalBB", sessionId: "OTHER-SESSION", branchId: "main"));

        var response = await _client.GetAsync($"/evals/scores/by-branch?sessionId={sid}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().NotBeNull();
        records!.Should().HaveCountGreaterThanOrEqualTo(2);
        records.Should().OnlyContain(r => r.GetProperty("sessionId").GetString() == sid);
    }

    [Fact]
    public async Task GET_evals_scores_byBranch_FiltersToSpecificBranch()
    {
        const string sid = "byBranch-session-specific";
        await SeedAsync(ScoreRecordFactory.Make("EvalBBF", sessionId: sid, branchId: "main"));
        await SeedAsync(ScoreRecordFactory.Make("EvalBBF", sessionId: sid, branchId: "fork-1"));

        var response = await _client.GetAsync($"/evals/scores/by-branch?sessionId={sid}&branchId=main");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().HaveCountGreaterThanOrEqualTo(1);
        records!.Should().OnlyContain(r => r.GetProperty("branchId").GetString() == "main");
    }

    [Fact]
    public async Task GET_evals_scores_byBranch_Returns200_WithEmpty_WhenSessionUnknown()
    {
        var response = await _client.GetAsync("/evals/scores/by-branch?sessionId=nonexistent-session-xyz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Trim().Should().Be("[]");
    }

    [Fact]
    public async Task GET_evals_scores_byBranch_Returns400_WhenSessionIdMissing()
    {
        var response = await _client.GetAsync("/evals/scores/by-branch");

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
            branchId = record.BranchId,
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
            branchId = record.BranchId,
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
    public async Task POST_evals_scores_CanBeReadBack_Via_GetScoresByBranch()
    {
        const string sid = "post-roundtrip-session";
        const string bid = "post-roundtrip-branch";
        var record = ScoreRecordFactory.Make("PostRoundtrip", sessionId: sid, branchId: bid);
        var body = new
        {
            evaluatorName = record.EvaluatorName,
            evaluatorVersion = record.EvaluatorVersion,
            result = record.Result,
            source = record.Source.ToString(),
            sessionId = sid,
            branchId = bid,
            turnIndex = record.TurnIndex,
            agentName = record.AgentName,
            turnDuration = record.TurnDuration.ToString(),
            samplingRate = record.SamplingRate,
            policy = record.Policy.ToString(),
            createdAt = record.CreatedAt,
        };

        var postResponse = await _client.PostAsJsonAsync("/evals/scores", body);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResponse = await _client.GetAsync($"/evals/scores/by-branch?sessionId={sid}&branchId={bid}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await getResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        records.Should().NotBeNull();
        records!.Should().HaveCountGreaterThanOrEqualTo(1);
        records.Should().Contain(r =>
            r.GetProperty("evaluatorName").GetString() == "PostRoundtrip" &&
            r.GetProperty("sessionId").GetString() == sid &&
            r.GetProperty("branchId").GetString() == bid);
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
            branchId = record.BranchId,
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
    // Category F — GET /evals/evaluators
    // =========================================================================

    [Fact]
    public async Task GET_evals_evaluators_Returns_Summary_ForSeededEvaluators()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalSummary_X", sessionId: "summ-s1", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalSummary_X", sessionId: "summ-s2", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalSummary_Y", sessionId: "summ-s3", passing: false));

        var response = await _client.GetAsync("/evals/evaluators");

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

        var response = await client.GetAsync("/evals/evaluators");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Trim().Should().Be("[]");
    }

    // =========================================================================
    // Category G — GET /evals/trend/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_trend_Returns_ScoreTrendShape()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(ScoreRecordFactory.MakeNumeric("EvalTrend", sessionId: "trend-s1", score: 3.0, createdAt: now.AddMinutes(-90)));
        await SeedAsync(ScoreRecordFactory.MakeNumeric("EvalTrend", sessionId: "trend-s2", score: 7.0, createdAt: now.AddMinutes(-30)));

        var from = now.AddHours(-2).ToString("O");
        var to = now.ToString("O");
        var url = $"/evals/trend/EvalTrend?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&bucketSize=PT1H";

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
        var url = $"/evals/trend/EvalTrendDefault?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_evals_trend_Returns400_WhenFromMissing()
    {
        var to = DateTimeOffset.UtcNow.ToString("O");
        var response = await _client.GetAsync($"/evals/trend/AnyEval?to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category H — GET /evals/pass-rate/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_passRate_Returns_PassRateObject_WithCorrectShape()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s1", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s2", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s3", passing: false));
        await SeedAsync(ScoreRecordFactory.Make("EvalPR", sessionId: "pr-s4", passing: true));

        var response = await _client.GetAsync("/evals/pass-rate/EvalPR");

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
        var response = await _client.GetAsync("/evals/pass-rate/EvalPassRateNoScores_XYZ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("passRate").GetDouble().Should().Be(0.0);
    }

    // =========================================================================
    // Category I — GET /evals/failure-rate/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_failureRate_Returns_FailureRateObject_WithCorrectShape()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalFR", sessionId: "fr-s1", passing: false));
        await SeedAsync(ScoreRecordFactory.Make("EvalFR", sessionId: "fr-s2", passing: true));

        var response = await _client.GetAsync("/evals/failure-rate/EvalFR");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("evaluatorName").GetString().Should().Be("EvalFR");
        doc.GetProperty("failureRate").GetDouble().Should().BeInRange(0.0, 1.0);
    }

    // =========================================================================
    // Category J — GET /evals/agent-comparison/{evaluatorName}
    // =========================================================================

    [Fact]
    public async Task GET_evals_agentComparison_Returns_DictionaryByAgentName()
    {
        await SeedAsync(ScoreRecordFactory.Make("EvalAC", agentName: "agent-alpha", sessionId: "ac-s1"));
        await SeedAsync(ScoreRecordFactory.Make("EvalAC", agentName: "agent-alpha", sessionId: "ac-s2"));
        await SeedAsync(ScoreRecordFactory.Make("EvalAC", agentName: "agent-beta", sessionId: "ac-s3"));

        var response = await _client.GetAsync("/evals/agent-comparison/EvalAC?agentNames=agent-alpha,agent-beta");

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
        var response = await _client.GetAsync("/evals/agent-comparison/EvalAC");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category K — GET /evals/branch-comparison
    // =========================================================================

    [Fact]
    public async Task GET_evals_branchComparison_Returns_BranchComparisonResult()
    {
        const string sid = "bc-session";
        await SeedAsync(ScoreRecordFactory.Make("EvalBC", sessionId: sid, branchId: "bc-main", passing: true));
        await SeedAsync(ScoreRecordFactory.Make("EvalBC", sessionId: sid, branchId: "bc-fork", passing: false));

        var url = $"/evals/branch-comparison?sessionId={sid}&branchId1=bc-main&branchId2=bc-fork&evaluatorNames=EvalBC";
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("sessionId").GetString().Should().Be(sid);
        doc.GetProperty("branchId1").GetString().Should().Be("bc-main");
        doc.GetProperty("branchId2").GetString().Should().Be("bc-fork");
        doc.TryGetProperty("branch1Scores", out _).Should().BeTrue();
        doc.TryGetProperty("branch2Scores", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GET_evals_branchComparison_Returns400_WhenRequiredParamMissing()
    {
        // branchId2 missing
        var response = await _client.GetAsync("/evals/branch-comparison?sessionId=s1&branchId1=b1&evaluatorNames=E1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // Category L — GET /evals/tool-usage
    // =========================================================================

    [Fact]
    public async Task GET_evals_toolUsage_Returns200_WithObjectResult()
    {
        var response = await _client.GetAsync("/evals/tool-usage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Empty store → empty object {} or populated object — either is valid
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        // Must be parseable JSON
        var act = () => JsonDocument.Parse(body);
        act.Should().NotThrow();
    }

    // =========================================================================
    // Category M — GET /evals/risk-autonomy
    // =========================================================================

    [Fact]
    public async Task GET_evals_riskAutonomy_Returns200_WithArrayResult()
    {
        var response = await _client.GetAsync("/evals/risk-autonomy");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // Must be a JSON array
        var doc = JsonDocument.Parse(body).RootElement;
        doc.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // =========================================================================
    // Category N — GET /evals/cost
    // =========================================================================

    [Fact]
    public async Task GET_evals_cost_Returns200_WithObjectResult()
    {
        var response = await _client.GetAsync("/evals/cost");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body).RootElement;
        doc.ValueKind.Should().Be(JsonValueKind.Object);
    }
}
