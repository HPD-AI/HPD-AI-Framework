using HPD.Agent.AspNetCore.EndpointMapping;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.RedTeam;
using HPD.Agent.Evaluations.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class EvalEndpoints
{
    internal static RouteGroupBuilder Map(IEndpointRouteBuilder endpoints, string routePrefix)
    {
        var scoreStore = endpoints.ServiceProvider.GetService<IScoreStore>();
        var datasetStore = endpoints.ServiceProvider.GetService<IDatasetStore>();

        var group = endpoints.MapGroup(routePrefix).WithTags("Evaluations");
        var analytics = group.MapGroup("/analytics");
        var runs = group.MapGroup("/runs");
        var datasets = group.MapGroup("/datasets");
        var redTeam = group.MapGroup("/red-team");

        // Score queries
        group.MapGet("/scores", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetScores(evaluatorName, from, to, scoreStore, ct))
            .WithName("GetScores")
            .WithSummary("Get scores by evaluator name");

        group.MapGet("/scores/by-thread", (string sessionId, string? threadId, CancellationToken ct)
            => GetScoresByThread(sessionId, threadId, scoreStore, ct))
            .WithName("GetScoresByThread")
            .WithSummary("Get scores filtered by session and thread");

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
        group.MapGet("/evaluators/catalog", () => GetEvaluatorCatalog())
            .WithName("GetEvaluatorCatalog")
            .WithSummary("Get built-in evaluator catalog metadata");

        analytics.MapGet("/evaluators", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetEvaluatorSummary(from, to, scoreStore, ct))
            .WithName("GetEvaluatorSummary")
            .WithSummary("Get evaluator summary analytics");

        analytics.MapGet("/trend/{evaluatorName}", (string evaluatorName, DateTimeOffset from, DateTimeOffset to,
            string? bucketSize, CancellationToken ct)
            => GetTrend(evaluatorName, from, to, bucketSize, scoreStore, ct))
            .WithName("GetTrend")
            .WithSummary("Get trend data for an evaluator");

        analytics.MapGet("/pass-rate/{evaluatorName}", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to,
            CancellationToken ct)
            => GetPassRate(evaluatorName, from, to, scoreStore, ct))
            .WithName("GetPassRate")
            .WithSummary("Get pass rate for an evaluator");

        analytics.MapGet("/failure-rate/{evaluatorName}", (string evaluatorName, DateTimeOffset? from, DateTimeOffset? to,
            CancellationToken ct)
            => GetFailureRate(evaluatorName, from, to, scoreStore, ct))
            .WithName("GetFailureRate")
            .WithSummary("Get failure rate for an evaluator");

        analytics.MapGet("/agent-comparison/{evaluatorName}", (string evaluatorName, string agentNames,
            DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetAgentComparison(evaluatorName, agentNames, from, to, scoreStore, ct))
            .WithName("GetAgentComparison")
            .WithSummary("Compare performance across agents");

        analytics.MapGet("/thread-comparison", (string sessionId, string threadId1, string threadId2,
            string evaluatorNames, CancellationToken ct)
            => GetThreadComparison(sessionId, threadId1, threadId2, evaluatorNames, scoreStore, ct))
            .WithName("GetThreadComparison")
            .WithSummary("Compare performance across threads");

        analytics.MapGet("/tool-usage", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetToolUsage(from, to, scoreStore, ct))
            .WithName("GetToolUsage")
            .WithSummary("Get tool usage summary");

        analytics.MapGet("/risk-autonomy", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetRiskAutonomy(from, to, scoreStore, ct))
            .WithName("GetRiskAutonomy")
            .WithSummary("Get risk autonomy distribution");

        analytics.MapGet("/cost", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetCost(from, to, scoreStore, ct))
            .WithName("GetCost")
            .WithSummary("Get cost breakdown");

        analytics.MapGet("/safety", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetSafetyAnalytics(from, to, scoreStore, ct))
            .WithName("GetSafetyAnalytics")
            .WithSummary("Get safety evaluator summary analytics");

        analytics.MapGet("/red-team", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetRedTeamAnalytics(from, to, scoreStore, ct))
            .WithName("GetRedTeamAnalytics")
            .WithSummary("Get red-team attack success analytics");

        analytics.MapGet("/red-team/by-plugin", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetRedTeamAttackSuccessByPlugin(from, to, scoreStore, ct))
            .WithName("GetRedTeamAttackSuccessByPlugin")
            .WithSummary("Get red-team attack success rate grouped by plugin");

        analytics.MapGet("/red-team/by-strategy", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetRedTeamAttackSuccessByStrategy(from, to, scoreStore, ct))
            .WithName("GetRedTeamAttackSuccessByStrategy")
            .WithSummary("Get red-team attack success rate grouped by strategy");

        analytics.MapGet("/red-team/findings", (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => GetRedTeamFindings(from, to, scoreStore, ct))
            .WithName("GetRedTeamFindings")
            .WithSummary("Get persisted red-team findings");

        redTeam.MapGet("/plugins", () => GetRedTeamPluginCatalog())
            .WithName("GetRedTeamPluginCatalog")
            .WithSummary("Get built-in red-team plugin catalog metadata");

        redTeam.MapGet("/strategies", () => GetRedTeamStrategyCatalog())
            .WithName("GetRedTeamStrategyCatalog")
            .WithSummary("Get built-in red-team strategy catalog metadata");

        // Full run reporting
        runs.MapGet("/", (string? executionName, string? scenarioName, string? iterationName, CancellationToken ct)
            => GetRuns(executionName, scenarioName, iterationName, scoreStore, ct))
            .WithName("GetEvaluationRuns")
            .WithSummary("Get stored evaluation run records");

        runs.MapDelete("/", (string? executionName, string? scenarioName, string? iterationName, CancellationToken ct)
            => DeleteRuns(executionName, scenarioName, iterationName, scoreStore, ct))
            .WithName("DeleteEvaluationRuns")
            .WithSummary("Delete stored evaluation run records");

        runs.MapGet("/executions", (int? count, CancellationToken ct)
            => GetRunExecutions(count, scoreStore, ct))
            .WithName("GetEvaluationRunExecutions")
            .WithSummary("Get latest evaluation execution names");

        runs.MapGet("/scenarios", (string executionName, CancellationToken ct)
            => GetRunScenarios(executionName, scoreStore, ct))
            .WithName("GetEvaluationRunScenarios")
            .WithSummary("Get scenario names for an evaluation execution");

        runs.MapGet("/iterations", (string executionName, string scenarioName, CancellationToken ct)
            => GetRunIterations(executionName, scenarioName, scoreStore, ct))
            .WithName("GetEvaluationRunIterations")
            .WithSummary("Get iteration names for an evaluation scenario");

        // Dataset registry. The HTTP mutation/import surface uses string inputs so it
        // remains AOT-friendly and avoids reflection over arbitrary user TInput types.
        datasets.MapGet("/", (CancellationToken ct)
            => ListDatasets(datasetStore, ct))
            .WithName("ListEvaluationDatasets")
            .WithSummary("List registered evaluation datasets");

        datasets.MapPost("/", (RegisterStringDatasetRequest request, CancellationToken ct)
            => RegisterStringDataset(request, datasetStore, ct))
            .WithName("RegisterStringEvaluationDataset")
            .WithSummary("Register an immutable string-input dataset version");

        datasets.MapGet("/{datasetId}", (string datasetId, CancellationToken ct)
            => GetDataset(datasetId, datasetStore, ct))
            .WithName("GetEvaluationDataset")
            .WithSummary("Get dataset registry metadata");

        datasets.MapGet("/{datasetId}/versions", (string datasetId, CancellationToken ct)
            => GetDatasetVersions(datasetId, datasetStore, ct))
            .WithName("GetEvaluationDatasetVersions")
            .WithSummary("List versions for a registered evaluation dataset");

        datasets.MapGet("/{datasetId}/versions/{version}", (string datasetId, string version, CancellationToken ct)
            => GetStringDatasetVersion(datasetId, version, datasetStore, ct))
            .WithName("GetStringEvaluationDatasetVersion")
            .WithSummary("Get a string-input dataset version");

        datasets.MapGet("/{datasetId}/active-cases", (string datasetId, DateTimeOffset at, CancellationToken ct)
            => GetActiveStringCases(datasetId, at, datasetStore, ct))
            .WithName("GetActiveStringEvaluationCases")
            .WithSummary("Get active string-input cases at a point in time");

        datasets.MapGet("/{datasetId}/cases/{caseId}/history", (string datasetId, string caseId, CancellationToken ct)
            => GetStringCaseHistory(datasetId, caseId, datasetStore, ct))
            .WithName("GetStringEvaluationCaseHistory")
            .WithSummary("Get SCD-2 history for a string-input evaluation case");

        datasets.MapGet("/{datasetId}/diff", (string datasetId, string fromVersion, string toVersion, CancellationToken ct)
            => CompareStringDatasetVersions(datasetId, fromVersion, toVersion, datasetStore, ct))
            .WithName("CompareStringEvaluationDatasetVersions")
            .WithSummary("Compare two string-input dataset versions");
        return group;
    }

    // Results.Problem() uses ProblemHttpResult → WriteAsJsonAsync → PipeWriter.UnflushedBytes,
    // which is not implemented by the TestServer response body. Use a plain 503 content result.
    private static ContentHttpResult NoStore() =>
        TypedResults.Content("No IScoreStore is registered.", "text/plain", statusCode: 503);

    private static ContentHttpResult NoDatasetStore() =>
        TypedResults.Content("No IDatasetStore is registered.", "text/plain", statusCode: 503);

    private static ContentHttpResult ServerError() =>
        TypedResults.Content("Evaluation storage operation failed.", "text/plain", statusCode: 500);

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<ScoreRecord>>, ContentHttpResult, ValidationProblem>> GetScoresByThread(
        string sessionId,
        string? threadId,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var records = new List<ScoreRecord>();
            await foreach (var r in scoreStore.GetScoresAsync(sessionId, threadId, ct))
                records.Add(r);
            return TypedResults.Ok(records);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
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
                ThreadId = request.ThreadId ?? string.Empty,
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    // ── Analytics ─────────────────────────────────────────────────────────────

    private static Ok<List<EvaluatorCatalogItem>> GetEvaluatorCatalog()
        => TypedResults.Ok(EvaluatorCatalog.All
            .Select(item => new EvaluatorCatalogItem(
                item.Id,
                item.Name,
                item.Category,
                item.Kind,
                item.MetricNames,
                item.DefaultPolicy.ToString(),
                item.RequiresJudge,
                item.RequiredContexts,
                item.Description))
            .ToList());

    private static async Task<Results<Ok<List<EvaluatorSummary>>, ContentHttpResult, ValidationProblem>> GetEvaluatorSummary(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var summary = await scoreStore.GetEvaluatorSummaryAsync(from, to, ct);
            return TypedResults.Ok(summary.ToList());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<ScoreTrend>, ContentHttpResult, ValidationProblem>> GetTrend(
        string evaluatorName,
        DateTimeOffset from,
        DateTimeOffset to,
        string? bucketSize,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        TimeSpan bucket;
        try
        {
            bucket = bucketSize is not null
                ? System.Xml.XmlConvert.ToTimeSpan(bucketSize)
                : TimeSpan.FromHours(1);
        }
        catch (FormatException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bucketSize"] = [ex.Message]
            });
        }

        try
        {
            var trend = await scoreStore.GetTrendAsync(evaluatorName, from, to, bucket, ct);
            return TypedResults.Ok(trend);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<RateResponse>, ContentHttpResult, ValidationProblem>> GetPassRate(
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
            return TypedResults.Ok(new RateResponse(evaluatorName, passRate));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<FailureRateResponse>, ContentHttpResult, ValidationProblem>> GetFailureRate(
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
            return TypedResults.Ok(new FailureRateResponse(evaluatorName, failureRate));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<Dictionary<string, ScoreAggregate>>, ContentHttpResult, ValidationProblem>> GetAgentComparison(
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
            return TypedResults.Ok(result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<ThreadComparisonResult>, ContentHttpResult, ValidationProblem>> GetThreadComparison(
        string sessionId,
        string threadId1,
        string threadId2,
        string evaluatorNames,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var names = evaluatorNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = await scoreStore.GetThreadComparisonAsync(sessionId, threadId1, threadId2, names, ct);
            return TypedResults.Ok(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<Dictionary<string, ToolUsageSummary>>, ContentHttpResult, ValidationProblem>> GetToolUsage(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetToolUsageSummaryAsync(from, to, ct);
            return TypedResults.Ok(result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<RiskAutonomyDataPoint>>, ContentHttpResult, ValidationProblem>> GetRiskAutonomy(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetRiskAutonomyDistributionAsync(from, to, ct);
            return TypedResults.Ok(result.ToList());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<Dictionary<string, double>>, ContentHttpResult, ValidationProblem>> GetCost(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetCostBreakdownAsync(from, to, ct);
            return TypedResults.Ok(result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<SafetyAnalyticsSummary>, ContentHttpResult, ValidationProblem>> GetSafetyAnalytics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var records = new List<ScoreRecord>();
            foreach (var item in EvaluatorCatalog.All.Where(item => item.Category == "Safety"))
            {
                await foreach (var record in scoreStore.GetScoresAsync(item.Name, from, to, ct))
                    records.Add(record);
            }

            var total = 0;
            var passed = 0;
            var failed = 0;
            var scoreSum = 0.0;
            var scoredCount = 0;
            var byCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bySeverity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byRecommendedAction = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                var metric = FindSafetyMetric(record);
                if (metric is null)
                    continue;

                total++;
                if (TryGetMetadata(metric, "safety-passed") is { } passedText &&
                    bool.TryParse(passedText, out var metricPassed))
                {
                    if (metricPassed) passed++;
                    else failed++;
                }
                else if (metric.Interpretation?.Failed == true)
                {
                    failed++;
                }
                else
                {
                    passed++;
                }

                if (record.Result.Metrics.Values.OfType<NumericMetric>().FirstOrDefault(m => m.Metadata?.ContainsKey("safety-category") == true) is { Value: { } score })
                {
                    scoreSum += score;
                    scoredCount++;
                }

                Increment(byCategory, TryGetMetadata(metric, "safety-category") ?? "unknown");
                Increment(bySeverity, TryGetMetadata(metric, "safety-severity") ?? "unknown");
                Increment(byRecommendedAction, TryGetMetadata(metric, "safety-recommended-action") ?? "unknown");
            }

            return TypedResults.Ok(new SafetyAnalyticsSummary(
                total,
                passed,
                failed,
                total == 0 ? 0.0 : (double)passed / total,
                scoredCount == 0 ? 0.0 : scoreSum / scoredCount,
                byCategory,
                bySeverity,
                byRecommendedAction));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static EvaluationMetric? FindSafetyMetric(ScoreRecord record)
    {
        foreach (var metric in record.Result.Metrics.Values)
        {
            if (metric.Metadata?.ContainsKey("safety-category") == true)
                return metric;
        }

        return null;
    }

    private static string? TryGetMetadata(EvaluationMetric metric, string key)
    {
        if (metric.Metadata?.TryGetValue(key, out var value) != true)
            return null;

        return value?.ToString();
    }

    private static void Increment(IDictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
    }

    private static async Task<Results<Ok<RedTeamAnalyticsSummary>, ContentHttpResult, ValidationProblem>> GetRedTeamAnalytics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var attackSuccessRate = await scoreStore.GetAttackSuccessRateAsync(from, to, ct);
            var byPlugin = await scoreStore.GetAttackSuccessRateByPluginAsync(from, to, ct);
            var byStrategy = await scoreStore.GetAttackSuccessRateByStrategyAsync(from, to, ct);
            var findings = await scoreStore.GetRedTeamFindingsAsync(from, to, ct);

            return TypedResults.Ok(new RedTeamAnalyticsSummary(
                attackSuccessRate,
                byPlugin.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
                byStrategy.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
                findings.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<Dictionary<string, double>>, ContentHttpResult, ValidationProblem>> GetRedTeamAttackSuccessByPlugin(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetAttackSuccessRateByPluginAsync(from, to, ct);
            return TypedResults.Ok(result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<Dictionary<string, double>>, ContentHttpResult, ValidationProblem>> GetRedTeamAttackSuccessByStrategy(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var result = await scoreStore.GetAttackSuccessRateByStrategyAsync(from, to, ct);
            return TypedResults.Ok(result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<RedTeamFinding>>, ContentHttpResult, ValidationProblem>> GetRedTeamFindings(
        DateTimeOffset? from,
        DateTimeOffset? to,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var findings = await scoreStore.GetRedTeamFindingsAsync(from, to, ct);
            return TypedResults.Ok(findings.ToList());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static Ok<List<RedTeamPluginCatalogItem>> GetRedTeamPluginCatalog()
        => TypedResults.Ok(RedTeamCatalog.Plugins.ToList());

    private static Ok<List<RedTeamStrategyCatalogItem>> GetRedTeamStrategyCatalog()
        => TypedResults.Ok(RedTeamCatalog.Strategies.ToList());

    // ── Full run reporting ───────────────────────────────────────────────────

    private static async Task<Results<Ok<List<EvaluationRunRecord>>, ContentHttpResult, ValidationProblem>> GetRuns(
        string? executionName,
        string? scenarioName,
        string? iterationName,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var records = new List<EvaluationRunRecord>();
            await foreach (var r in scoreStore.GetRunsAsync(executionName, scenarioName, iterationName, ct))
                records.Add(r);
            return TypedResults.Ok(records);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<NoContent, ContentHttpResult, ValidationProblem>> DeleteRuns(
        string? executionName,
        string? scenarioName,
        string? iterationName,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            await scoreStore.DeleteRunsAsync(executionName, scenarioName, iterationName, ct);
            return TypedResults.NoContent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<string>>, ContentHttpResult, ValidationProblem>> GetRunExecutions(
        int? count,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var names = new List<string>();
            await foreach (var name in scoreStore.GetLatestRunExecutionNamesAsync(count, ct))
                names.Add(name);
            return TypedResults.Ok(names);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<string>>, ContentHttpResult, ValidationProblem>> GetRunScenarios(
        string executionName,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var names = new List<string>();
            await foreach (var name in scoreStore.GetRunScenarioNamesAsync(executionName, ct))
                names.Add(name);
            return TypedResults.Ok(names);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<string>>, ContentHttpResult, ValidationProblem>> GetRunIterations(
        string executionName,
        string scenarioName,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        if (scoreStore is null) return NoStore();
        try
        {
            var names = new List<string>();
            await foreach (var name in scoreStore.GetRunIterationNamesAsync(executionName, scenarioName, ct))
                names.Add(name);
            return TypedResults.Ok(names);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    // ── Dataset registry ─────────────────────────────────────────────────────

    private static async Task<Results<Ok<List<DatasetRecord>>, ContentHttpResult, ValidationProblem>> ListDatasets(
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var records = new List<DatasetRecord>();
            await foreach (var record in datasetStore.ListDatasetsAsync(ct))
                records.Add(record);
            return TypedResults.Ok(records);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Created<DatasetVersionRecord>, ContentHttpResult, ValidationProblem>> RegisterStringDataset(
        RegisterStringDatasetRequest request,
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var dataset = new Dataset<string>
            {
                DatasetId = request.DatasetId,
                Version = request.Version,
                Cases = request.Cases.Select(c => new EvalCase<string>
                {
                    CaseId = c.CaseId,
                    Name = c.Name,
                    Version = c.Version,
                    ValidFrom = c.ValidFrom,
                    ValidTo = c.ValidTo,
                    Input = c.Input ?? string.Empty,
                    GroundTruth = c.GroundTruth,
                }).ToList(),
            };

            var record = await datasetStore.RegisterDatasetVersionAsync(
                dataset,
                new DatasetRegistrationOptions<string>
                {
                    Description = request.Description,
                    RegisteredAt = request.RegisteredAt,
                },
                ct);

            return TypedResults.Created($"/evals/datasets/{record.DatasetId}/versions/{record.Version}", record);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["RegisterDatasetError"] = [ex.Message]
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<DatasetRecord>, NotFound, ContentHttpResult, ValidationProblem>> GetDataset(
        string datasetId,
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var record = await datasetStore.GetDatasetAsync(datasetId, ct);
            return record is null ? TypedResults.NotFound() : TypedResults.Ok(record);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<DatasetVersionRecord>>, ContentHttpResult, ValidationProblem>> GetDatasetVersions(
        string datasetId,
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var records = new List<DatasetVersionRecord>();
            await foreach (var record in datasetStore.GetDatasetVersionsAsync(datasetId, ct))
                records.Add(record);
            return TypedResults.Ok(records);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<StringDatasetVersionResponse>, NotFound, ContentHttpResult, ValidationProblem>> GetStringDatasetVersion(
        string datasetId,
        string version,
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var dataset = await datasetStore.GetDatasetVersionAsync<string>(datasetId, version, ct);
            return dataset is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(StringDatasetVersionResponse.From(dataset));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<StringEvalCaseDto>>, ContentHttpResult, ValidationProblem>> GetActiveStringCases(
        string datasetId,
        DateTimeOffset at,
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var cases = new List<StringEvalCaseDto>();
            await foreach (var evalCase in datasetStore.GetActiveCasesAsync<string>(datasetId, at, ct))
                cases.Add(StringEvalCaseDto.From(evalCase));
            return TypedResults.Ok(cases);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<List<StringEvalCaseDto>>, ContentHttpResult, ValidationProblem>> GetStringCaseHistory(
        string datasetId,
        string caseId,
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var cases = new List<StringEvalCaseDto>();
            await foreach (var evalCase in datasetStore.GetCaseHistoryAsync<string>(datasetId, caseId, ct))
                cases.Add(StringEvalCaseDto.From(evalCase));
            return TypedResults.Ok(cases);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }

    private static async Task<Results<Ok<StringDatasetDiffResponse>, NotFound, ContentHttpResult, ValidationProblem>> CompareStringDatasetVersions(
        string datasetId,
        string fromVersion,
        string toVersion,
        IDatasetStore? datasetStore,
        CancellationToken ct)
    {
        if (datasetStore is null) return NoDatasetStore();
        try
        {
            var diff = await datasetStore.CompareVersionsAsync<string>(datasetId, fromVersion, toVersion, ct);
            return TypedResults.Ok(StringDatasetDiffResponse.From(diff));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServerError();
        }
    }
}

internal sealed record RateResponse(string EvaluatorName, double PassRate);

internal sealed record FailureRateResponse(string EvaluatorName, double FailureRate);

internal sealed record EvaluatorCatalogItem(
    string Id,
    string Name,
    string Category,
    string Kind,
    IReadOnlyList<string> MetricNames,
    string DefaultPolicy,
    bool RequiresJudge,
    IReadOnlyList<string> RequiredContexts,
    string Description);

internal sealed record SafetyAnalyticsSummary(
    int TotalCount,
    int PassedCount,
    int FailedCount,
    double PassRate,
    double AverageSafetyScore,
    IReadOnlyDictionary<string, int> ByCategory,
    IReadOnlyDictionary<string, int> BySeverity,
    IReadOnlyDictionary<string, int> ByRecommendedAction);

internal sealed record RedTeamAnalyticsSummary(
    double AttackSuccessRate,
    IReadOnlyDictionary<string, double> AttackSuccessRateByPlugin,
    IReadOnlyDictionary<string, double> AttackSuccessRateByStrategy,
    int FindingCount);

internal sealed record RedTeamPluginCatalogItem(
    string Id,
    string DisplayName,
    string Category);

internal sealed record RedTeamStrategyCatalogItem(
    string Id,
    string DisplayName);

internal static class RedTeamCatalog
{
    internal static readonly IReadOnlyList<RedTeamPluginCatalogItem> Plugins =
        RedTeamPlugins.Catalog
            .Select(plugin => new RedTeamPluginCatalogItem(
                plugin.Id,
                plugin.DisplayName,
                plugin.Category.ToString()))
            .ToArray();

    internal static readonly IReadOnlyList<RedTeamStrategyCatalogItem> Strategies =
        RedTeamStrategies.Catalog
            .Select(strategy => new RedTeamStrategyCatalogItem(
                strategy.Id,
                strategy.DisplayName))
            .ToArray();
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
    public string? ThreadId { get; init; }
    public int TurnIndex { get; init; }
    public string? AgentName { get; init; }
    public string? TurnDuration { get; init; }
    public double SamplingRate { get; init; }
    public string? Policy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class RegisterStringDatasetRequest
{
    public string? DatasetId { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? RegisteredAt { get; init; }
    public IReadOnlyList<StringEvalCaseDto> Cases { get; init; } = [];
}

internal sealed class StringDatasetVersionResponse
{
    public string? DatasetId { get; init; }
    public string? Version { get; init; }
    public IReadOnlyList<StringEvalCaseDto> Cases { get; init; } = [];

    public static StringDatasetVersionResponse From(Dataset<string> dataset) => new()
    {
        DatasetId = dataset.DatasetId,
        Version = dataset.Version,
        Cases = dataset.Cases.Select(StringEvalCaseDto.From).ToList(),
    };
}

internal sealed class StringDatasetDiffResponse
{
    public string DatasetId { get; init; } = string.Empty;
    public string FromVersion { get; init; } = string.Empty;
    public string ToVersion { get; init; } = string.Empty;
    public IReadOnlyList<StringEvalCaseDto> Added { get; init; } = [];
    public IReadOnlyList<StringEvalCaseDto> Removed { get; init; } = [];
    public IReadOnlyList<StringDatasetCaseChangeDto> Changed { get; init; } = [];

    public static StringDatasetDiffResponse From(DatasetVersionDiff<string> diff) => new()
    {
        DatasetId = diff.DatasetId,
        FromVersion = diff.FromVersion,
        ToVersion = diff.ToVersion,
        Added = diff.Added.Select(StringEvalCaseDto.From).ToList(),
        Removed = diff.Removed.Select(StringEvalCaseDto.From).ToList(),
        Changed = diff.Changed.Select(StringDatasetCaseChangeDto.From).ToList(),
    };
}

internal sealed class StringDatasetCaseChangeDto
{
    public string CaseId { get; init; } = string.Empty;
    public StringEvalCaseDto Before { get; init; } = new();
    public StringEvalCaseDto After { get; init; } = new();

    public static StringDatasetCaseChangeDto From(DatasetCaseChange<string> change) => new()
    {
        CaseId = change.CaseId,
        Before = StringEvalCaseDto.From(change.Before),
        After = StringEvalCaseDto.From(change.After),
    };
}

internal sealed class StringEvalCaseDto
{
    public string? CaseId { get; init; }
    public string? Name { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
    public string? Input { get; init; }
    public string? GroundTruth { get; init; }

    public static StringEvalCaseDto From(EvalCase<string> evalCase) => new()
    {
        CaseId = evalCase.CaseId,
        Name = evalCase.Name,
        Version = evalCase.Version,
        ValidFrom = evalCase.ValidFrom,
        ValidTo = evalCase.ValidTo,
        Input = evalCase.Input,
        GroundTruth = evalCase.GroundTruth,
    };
}
