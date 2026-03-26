using HPD.Agent.AspNetCore.EndpointMapping;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class EvalEndpoints
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var scoreStore = endpoints.ServiceProvider.GetService<IScoreStore>();

        var group = endpoints.MapGroup("/evals").WithTags("Evaluations");

        // Score queries
        group.MapGet("/scores", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetScores(evaluatorName, from, to, scoreStore, ct))
            .WithName("GetScores")
            .WithSummary("Get scores by evaluator name");

        group.MapGet("/scores/by-branch", (string sessionId, string? branchId, CancellationToken ct)
            => GetScoresByBranch(sessionId, branchId, scoreStore, ct))
            .WithName("GetScoresByBranch")
            .WithSummary("Get scores filtered by session and branch");

        group.MapGet("/scores/by-version", (string evaluatorName, string version, CancellationToken ct)
            => GetScoresByVersion(evaluatorName, version, scoreStore, ct))
            .WithName("GetScoresByVersion")
            .WithSummary("Get scores by evaluator name and version");

        // Accepts a flat DTO rather than ScoreRecord directly — EvaluationResult is not
        // JSON-deserializable from external callers; enums are sent as strings.
        group.MapPost("/scores", (WriteScoreRequest request, CancellationToken ct)
            => WriteScore(request, scoreStore, ct))
            .WithName("WriteScore")
            .WithSummary("Write a score record");

        // Analytics
        group.MapGet("/evaluators", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetEvaluatorSummary(from, to, scoreStore, ct))
            .WithName("GetEvaluatorSummary")
            .WithSummary("Get evaluator summary analytics");

        group.MapGet("/trend/{evaluatorName}", (string evaluatorName, DateTimeOffset from, DateTimeOffset to,
            string? bucketSize, CancellationToken ct)
            => GetTrend(evaluatorName, from, to, bucketSize, scoreStore, ct))
            .WithName("GetTrend")
            .WithSummary("Get trend data for an evaluator");

        group.MapGet("/pass-rate/{evaluatorName}", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to,
            CancellationToken ct)
            => GetPassRate(evaluatorName, from, to, scoreStore, ct))
            .WithName("GetPassRate")
            .WithSummary("Get pass rate for an evaluator");

        group.MapGet("/failure-rate/{evaluatorName}", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to,
            CancellationToken ct)
            => GetFailureRate(evaluatorName, from, to, scoreStore, ct))
            .WithName("GetFailureRate")
            .WithSummary("Get failure rate for an evaluator");

        group.MapGet("/agent-comparison/{evaluatorName}", (string evaluatorName, string agentNames,
            DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetAgentComparison(evaluatorName, agentNames, from, to, scoreStore, ct))
            .WithName("GetAgentComparison")
            .WithSummary("Compare performance across agents");

        group.MapGet("/branch-comparison", (string sessionId, string branchId1, string branchId2,
            string evaluatorNames, CancellationToken ct)
            => GetBranchComparison(sessionId, branchId1, branchId2, evaluatorNames, scoreStore, ct))
            .WithName("GetBranchComparison")
            .WithSummary("Compare performance across branches");

        group.MapGet("/tool-usage", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetToolUsage(from, to, scoreStore, ct))
            .WithName("GetToolUsage")
            .WithSummary("Get tool usage summary");

        group.MapGet("/risk-autonomy", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetRiskAutonomy(from, to, scoreStore, ct))
            .WithName("GetRiskAutonomy")
            .WithSummary("Get risk autonomy distribution");

        group.MapGet("/cost", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetCost(from, to, scoreStore, ct))
            .WithName("GetCost")
            .WithSummary("Get cost breakdown");
    }

    // Results.Problem() uses ProblemHttpResult → WriteAsJsonAsync → PipeWriter.UnflushedBytes,
    // which is not implemented by the TestServer response body. Use a plain 503 content result.
    private static ContentHttpResult NoStore() =>
        TypedResults.Content("No IScoreStore is registered.", "text/plain", statusCode: 503);

    // ── Score queries ─────────────────────────────────────────────────────────

    private static async Task<Results<Ok<List<ScoreRecord>>, ContentHttpResult, ValidationProblem>> GetScores(
        string evaluatorName,
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var records = new List<ScoreRecord>();
            await foreach (var r in scoreStore.GetScoresAsync(evaluatorName, from, to, ct))
                records.Add(r);
            return TypedResults.Ok(records);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetScoresError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<List<ScoreRecord>>, ContentHttpResult, ValidationProblem>> GetScoresByBranch(
        string sessionId,
        string? branchId,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var records = new List<ScoreRecord>();
            await foreach (var r in scoreStore.GetScoresAsync(sessionId, branchId, ct))
                records.Add(r);
            return TypedResults.Ok(records);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetScoresByBranchError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<List<ScoreRecord>>, ContentHttpResult, ValidationProblem>> GetScoresByVersion(
        string evaluatorName,
        string version,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var records = new List<ScoreRecord>();
            await foreach (var r in scoreStore.GetScoresByVersionAsync(evaluatorName, version, ct))
                records.Add(r);
            return TypedResults.Ok(records);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetScoresByVersionError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Created<ScoreRecord>, ContentHttpResult, ValidationProblem>> WriteScore(
        WriteScoreRequest request,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            _ = Enum.TryParse<EvaluationSource>(request.Source, ignoreCase: true, out var source);
            _ = Enum.TryParse<EvalPolicy>(request.Policy, ignoreCase: true, out var policy);
            _ = TimeSpan.TryParse(request.TurnDuration, out var turnDuration);

            var stored = new ScoreRecord
            {
                Id = Guid.NewGuid().ToString(),
                EvaluatorName = request.EvaluatorName ?? string.Empty,
                EvaluatorVersion = request.EvaluatorVersion ?? string.Empty,
                Result = request.Result ?? new Microsoft.Extensions.AI.Evaluation.EvaluationResult([]),
                Source = source,
                SessionId = request.SessionId ?? string.Empty,
                BranchId = request.BranchId ?? string.Empty,
                TurnIndex = request.TurnIndex,
                AgentName = request.AgentName ?? string.Empty,
                TurnDuration = turnDuration,
                SamplingRate = request.SamplingRate,
                Policy = policy,
                CreatedAt = request.CreatedAt == default ? DateTimeOffset.UtcNow : request.CreatedAt,
            };
            await scoreStore.WriteScoreAsync(stored, ct);
            return TypedResults.Created("", stored);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["WriteScoreError"] = [ex.Message]
            });
        }
    }

    // ── Analytics ─────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetEvaluatorSummary(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var summary = await scoreStore.GetEvaluatorSummaryAsync(from, to, ct);
            return TypedResults.Ok((object)summary);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetEvaluatorSummaryError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetTrend(
        string evaluatorName,
        DateTimeOffset from,
        DateTimeOffset to,
        string? bucketSize,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var bucket = bucketSize is not null
                ? System.Xml.XmlConvert.ToTimeSpan(bucketSize)
                : TimeSpan.FromHours(1);
            var trend = await scoreStore.GetTrendAsync(evaluatorName, from, to, bucket, ct);
            return TypedResults.Ok((object)trend);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetTrendError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetPassRate(
        string evaluatorName,
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var passRate = await scoreStore.GetPassRateAsync(evaluatorName, from, to, ct);
            return TypedResults.Ok((object)new { evaluatorName, passRate });
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetPassRateError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetFailureRate(
        string evaluatorName,
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var failureRate = await scoreStore.GetFailureRateAsync(evaluatorName, from, to, ct);
            return TypedResults.Ok((object)new { evaluatorName, failureRate });
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetFailureRateError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetAgentComparison(
        string evaluatorName,
        string agentNames,
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var names = agentNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = await scoreStore.GetAgentComparisonAsync(evaluatorName, names, from, to, ct);
            return TypedResults.Ok((object)result);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetAgentComparisonError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetBranchComparison(
        string sessionId,
        string branchId1,
        string branchId2,
        string evaluatorNames,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var names = evaluatorNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = await scoreStore.GetBranchComparisonAsync(sessionId, branchId1, branchId2, names, ct);
            return TypedResults.Ok((object)result);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetBranchComparisonError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetToolUsage(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetToolUsageSummaryAsync(from, to, ct);
            return TypedResults.Ok((object)result);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetToolUsageError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetRiskAutonomy(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetRiskAutonomyDistributionAsync(from, to, ct);
            return TypedResults.Ok((object)result);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetRiskAutonomyError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<object>, ContentHttpResult, ValidationProblem>> GetCost(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetCostBreakdownAsync(from, to, ct);
            return TypedResults.Ok((object)result);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetCostError"] = [ex.Message]
            });
        }
    }
}

/// <summary>
/// Flat DTO for POST /evals/scores. Uses string for enum fields so external callers
/// can send "Test", "TrackTrend", "00:00:01" without requiring the M.E.AI.Evaluation
/// type system on the client side.
/// </summary>
internal sealed class WriteScoreRequest
{
    public string? EvaluatorName { get; init; }
    public string? EvaluatorVersion { get; init; }
    public Microsoft.Extensions.AI.Evaluation.EvaluationResult? Result { get; init; }
    public string? Source { get; init; }
    public string? SessionId { get; init; }
    public string? BranchId { get; init; }
    public int TurnIndex { get; init; }
    public string? AgentName { get; init; }
    public string? TurnDuration { get; init; }
    public double SamplingRate { get; init; }
    public string? Policy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
