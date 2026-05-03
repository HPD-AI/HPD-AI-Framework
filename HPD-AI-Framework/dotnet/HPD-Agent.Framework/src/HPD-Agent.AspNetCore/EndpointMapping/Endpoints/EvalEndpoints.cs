using HPD.Agent.AspNetCore.EndpointMapping;
using HPD.Agent.Evaluations.Batch;
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
        var datasetStore = endpoints.ServiceProvider.GetService<IDatasetStore>();

        var group = endpoints.MapGroup("/evals").WithTags("Evaluations");
        var analytics = group.MapGroup("/analytics");
        var runs = group.MapGroup("/runs");
        var datasets = group.MapGroup("/datasets");

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

        analytics.MapGet("/branch-comparison", (string sessionId, string branchId1, string branchId2,
            string evaluatorNames, CancellationToken ct)
            => GetBranchComparison(sessionId, branchId1, branchId2, evaluatorNames, scoreStore, ct))
            .WithName("GetBranchComparison")
            .WithSummary("Compare performance across branches");

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
    }

    // Results.Problem() uses ProblemHttpResult → WriteAsJsonAsync → PipeWriter.UnflushedBytes,
    // which is not implemented by the TestServer response body. Use a plain 503 content result.
    private static ContentHttpResult NoStore() =>
        TypedResults.Content("No IScoreStore is registered.", "text/plain", statusCode: 503);

    private static ContentHttpResult NoDatasetStore() =>
        TypedResults.Content("No IDatasetStore is registered.", "text/plain", statusCode: 503);

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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetEvaluatorSummaryError"] = [ex.Message]
            });
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
        try
        {
            var bucket = bucketSize is not null
                ? System.Xml.XmlConvert.ToTimeSpan(bucketSize)
                : TimeSpan.FromHours(1);
            var trend = await scoreStore.GetTrendAsync(evaluatorName, from, to, bucket, ct);
            return TypedResults.Ok(trend);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetTrendError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetPassRateError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetFailureRateError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetAgentComparisonError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<BranchComparisonResult>, ContentHttpResult, ValidationProblem>> GetBranchComparison(
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
            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetBranchComparisonError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetToolUsageError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetRiskAutonomyError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetCostError"] = [ex.Message]
            });
        }
    }

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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetRunsError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteRunsError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetRunExecutionsError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetRunScenariosError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetRunIterationsError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ListDatasetsError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["RegisterDatasetError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetDatasetError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetDatasetVersionsError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetDatasetVersionError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetActiveCasesError"] = [ex.Message]
            });
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetCaseHistoryError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<StringDatasetDiffResponse>, ContentHttpResult, ValidationProblem>> CompareStringDatasetVersions(
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
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["CompareDatasetVersionsError"] = [ex.Message]
            });
        }
    }
}

internal sealed record RateResponse(string EvaluatorName, double PassRate);

internal sealed record FailureRateResponse(string EvaluatorName, double FailureRate);

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
