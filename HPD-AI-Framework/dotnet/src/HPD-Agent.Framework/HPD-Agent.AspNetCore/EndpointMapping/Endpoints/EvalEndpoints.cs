using HPD.Agent.AspNetCore.EndpointMapping;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
            => GetScores(evaluatorName, from, to, scoreStore, ct));

        group.MapGet("/scores/by-branch", (string sessionId, string? branchId, CancellationToken ct)
            => GetScoresByBranch(sessionId, branchId, scoreStore, ct));

        group.MapGet("/scores/by-version", (string evaluatorName, string version, CancellationToken ct)
            => GetScoresByVersion(evaluatorName, version, scoreStore, ct));

        // Accepts a flat DTO rather than ScoreRecord directly — EvaluationResult is not
        // JSON-deserializable from external callers; enums are sent as strings.
        group.MapPost("/scores", (WriteScoreRequest request, CancellationToken ct)
            => WriteScore(request, scoreStore, ct));

        // Analytics
        group.MapGet("/evaluators", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetEvaluatorSummary(from, to, scoreStore, ct));

        group.MapGet("/trend/{evaluatorName}", (string evaluatorName, DateTimeOffset from, DateTimeOffset to,
            string? bucketSize, CancellationToken ct)
            => GetTrend(evaluatorName, from, to, bucketSize, scoreStore, ct));

        group.MapGet("/pass-rate/{evaluatorName}", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to,
            CancellationToken ct)
            => GetPassRate(evaluatorName, from, to, scoreStore, ct));

        group.MapGet("/failure-rate/{evaluatorName}", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to,
            CancellationToken ct)
            => GetFailureRate(evaluatorName, from, to, scoreStore, ct));

        group.MapGet("/agent-comparison/{evaluatorName}", (string evaluatorName, string agentNames,
            DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetAgentComparison(evaluatorName, agentNames, from, to, scoreStore, ct));

        group.MapGet("/branch-comparison", (string sessionId, string branchId1, string branchId2,
            string evaluatorNames, CancellationToken ct)
            => GetBranchComparison(sessionId, branchId1, branchId2, evaluatorNames, scoreStore, ct));

        group.MapGet("/tool-usage", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetToolUsage(from, to, scoreStore, ct));

        group.MapGet("/risk-autonomy", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetRiskAutonomy(from, to, scoreStore, ct));

        group.MapGet("/cost", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetCost(from, to, scoreStore, ct));
    }

    // Results.Problem() uses ProblemHttpResult → WriteAsJsonAsync → PipeWriter.UnflushedBytes,
    // which is not implemented by the TestServer response body. Use a plain 503 content result.
    private static IResult NoStore() =>
        Results.Content("No IScoreStore is registered.", "text/plain", statusCode: 503);

    // ── Score queries ─────────────────────────────────────────────────────────

    private static async Task<IResult> GetScores(
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
            return ErrorResponses.Json(records);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetScoresError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetScoresByBranch(
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
            return ErrorResponses.Json(records);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetScoresByBranchError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetScoresByVersion(
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
            return ErrorResponses.Json(records);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetScoresByVersionError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> WriteScore(
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
            return ErrorResponses.Json(stored, 201);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["WriteScoreError"] = [ex.Message]
            });
        }
    }

    // ── Analytics ─────────────────────────────────────────────────────────────

    private static async Task<IResult> GetEvaluatorSummary(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var summary = await scoreStore.GetEvaluatorSummaryAsync(from, to, ct);
            return ErrorResponses.Json(summary);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetEvaluatorSummaryError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetTrend(
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
            return ErrorResponses.Json(trend);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetTrendError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetPassRate(
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
            return ErrorResponses.Json(new { evaluatorName, passRate });
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetPassRateError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetFailureRate(
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
            return ErrorResponses.Json(new { evaluatorName, failureRate });
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetFailureRateError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetAgentComparison(
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
            return ErrorResponses.Json(result);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetAgentComparisonError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetBranchComparison(
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
            return ErrorResponses.Json(result);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetBranchComparisonError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetToolUsage(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetToolUsageSummaryAsync(from, to, ct);
            return ErrorResponses.Json(result);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetToolUsageError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetRiskAutonomy(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetRiskAutonomyDistributionAsync(from, to, ct);
            return ErrorResponses.Json(result);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetRiskAutonomyError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetCost(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetCostBreakdownAsync(from, to, ct);
            return ErrorResponses.Json(result);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
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
