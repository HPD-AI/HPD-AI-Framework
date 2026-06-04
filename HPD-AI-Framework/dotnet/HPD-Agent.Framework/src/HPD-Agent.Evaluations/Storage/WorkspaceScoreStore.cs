// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Workspace-backed score store. Score records and evaluation runs are stored as
/// typed documents attached to a workspace evaluation-results space.
/// </summary>
public sealed class WorkspaceScoreStore : IScoreStore
{
    public const string EvaluationResultsKind = "eval_results";
    public const string EvaluationResultsExternalId = "default";
    public const string ScoreRecordRole = "eval_score_record";
    public const string RunRecordRole = "eval_run_record";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IWorkspaceStore _workspace;
    private readonly WorkspacePrincipalRef _principal;

    public WorkspaceScoreStore(
        IWorkspaceStore workspace,
        WorkspacePrincipalRef? principal = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _principal = principal ?? WorkspacePrincipalRef.System;
    }

    public async ValueTask WriteScoreAsync(ScoreRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        var space = await GetOrCreateResultsSpaceAsync(ct).ConfigureAwait(false);
        await WriteDocumentAsync(
            space.Id,
            ScoreRecordRole,
            $"{NormalizeId(record.Id)}.json",
            ScoreRecordDto.From(record),
            ScoreMetadata(record),
            ct)
            .ConfigureAwait(false);
    }

    public async ValueTask WriteRunAsync(EvaluationRunRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        var space = await GetOrCreateResultsSpaceAsync(ct).ConfigureAwait(false);
        await WriteDocumentAsync(
            space.Id,
            RunRecordRole,
            $"{NormalizeId(record.Id)}.json",
            RunRecordDto.From(record),
            RunMetadata(record),
            ct)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string sessionId,
        string? branchId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var record in store.GetScoresAsync(sessionId, branchId, ct).ConfigureAwait(false))
            yield return record;
    }

    public async IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var record in store.GetScoresAsync(evaluatorName, from, to, ct).ConfigureAwait(false))
            yield return record;
    }

    public async IAsyncEnumerable<EvaluationRunRecord> GetRunsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var record in store.GetRunsAsync(executionName, scenarioName, iterationName, ct).ConfigureAwait(false))
            yield return record;
    }

    public async ValueTask DeleteRunsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var space = await FindResultsSpaceAsync(ct).ConfigureAwait(false);
        if (space is null)
            return;

        var attachments = await ListAttachmentsAsync(space.Id, RunRecordRole, ct).ConfigureAwait(false);
        foreach (var attachment in attachments)
        {
            var run = await LoadRunAsync(attachment, ct).ConfigureAwait(false);
            if (run is null || !RunMatches(run, executionName, scenarioName, iterationName))
                continue;

            await _workspace.DetachContentAsync(
                _principal,
                space.Id,
                attachment.Id,
                attachment.Version,
                ct).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<string> GetLatestRunExecutionNamesAsync(
        int? count = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var name in store.GetLatestRunExecutionNamesAsync(count, ct).ConfigureAwait(false))
            yield return name;
    }

    public async IAsyncEnumerable<string> GetRunScenarioNamesAsync(
        string executionName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var name in store.GetRunScenarioNamesAsync(executionName, ct).ConfigureAwait(false))
            yield return name;
    }

    public async IAsyncEnumerable<string> GetRunIterationNamesAsync(
        string executionName,
        string scenarioName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var name in store.GetRunIterationNamesAsync(executionName, scenarioName, ct).ConfigureAwait(false))
            yield return name;
    }

    public async ValueTask<ScoreTrend> GetTrendAsync(
        string evaluatorName,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan bucketSize,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetTrendAsync(evaluatorName, from, to, bucketSize, ct)
            .ConfigureAwait(false);

    public async ValueTask<double> GetPassRateAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetPassRateAsync(evaluatorName, from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<IDictionary<string, ScoreAggregate>> GetAgentComparisonAsync(
        string evaluatorName,
        IEnumerable<string> agentNames,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetAgentComparisonAsync(evaluatorName, agentNames, from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<BranchComparisonResult> GetBranchComparisonAsync(
        string sessionId,
        string branchId1,
        string branchId2,
        IEnumerable<string> evaluatorNames,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetBranchComparisonAsync(sessionId, branchId1, branchId2, evaluatorNames, ct)
            .ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<EvaluatorSummary>> GetEvaluatorSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetEvaluatorSummaryAsync(from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<double> GetFailureRateAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetFailureRateAsync(evaluatorName, from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<IDictionary<string, double>> GetCostBreakdownAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetCostBreakdownAsync(from, to, ct)
            .ConfigureAwait(false);

    public async IAsyncEnumerable<ScoreRecord> GetScoresByVersionAsync(
        string evaluatorName,
        string version,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var record in store.GetScoresByVersionAsync(evaluatorName, version, ct).ConfigureAwait(false))
            yield return record;
    }

    public async ValueTask<IDictionary<string, ToolUsageSummary>> GetToolUsageSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetToolUsageSummaryAsync(from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<RiskAutonomyDataPoint>> GetRiskAutonomyDistributionAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetRiskAutonomyDistributionAsync(from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<double> GetAttackSuccessRateAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetAttackSuccessRateAsync(from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<IDictionary<string, double>> GetAttackSuccessRateByPluginAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetAttackSuccessRateByPluginAsync(from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<IDictionary<string, double>> GetAttackSuccessRateByStrategyAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetAttackSuccessRateByStrategyAsync(from, to, ct)
            .ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<RedTeamFinding>> GetRedTeamFindingsAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await (await HydrateAsync(ct).ConfigureAwait(false))
            .GetRedTeamFindingsAsync(from, to, ct)
            .ConfigureAwait(false);

    public ValueTask DeleteResultsAsync(
        string? executionName,
        string? scenarioName,
        string? iterationName,
        CancellationToken ct = default)
        => DeleteRunsAsync(executionName, scenarioName, iterationName, ct);

    public IAsyncEnumerable<string> GetLatestExecutionNamesAsync(
        int? maxCount = null,
        CancellationToken ct = default)
        => GetLatestRunExecutionNamesAsync(maxCount, ct);

    public async IAsyncEnumerable<string> GetScenarioNamesAsync(
        string? executionName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var name in store.GetScenarioNamesAsync(executionName, ct).ConfigureAwait(false))
            yield return name;
    }

    public async IAsyncEnumerable<string> GetIterationNamesAsync(
        string? executionName,
        string? scenarioName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var name in store.GetIterationNamesAsync(executionName, scenarioName, ct).ConfigureAwait(false))
            yield return name;
    }

    public async IAsyncEnumerable<ScenarioRunResult> ReadResultsAsync(
        string? executionName,
        string? scenarioName,
        string? iterationName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var store = await HydrateAsync(ct).ConfigureAwait(false);
        await foreach (var result in store.ReadResultsAsync(executionName, scenarioName, iterationName, ct).ConfigureAwait(false))
            yield return result;
    }

    public async ValueTask WriteResultsAsync(
        IEnumerable<ScenarioRunResult> results,
        CancellationToken ct = default)
    {
        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();
            await WriteRunAsync(EvaluationRunRecord.FromScenarioRunResult(result), ct).ConfigureAwait(false);
        }
    }

    private async Task<InMemoryScoreStore> HydrateAsync(CancellationToken ct)
    {
        var store = new InMemoryScoreStore();
        var space = await FindResultsSpaceAsync(ct).ConfigureAwait(false);
        if (space is null)
            return store;

        foreach (var attachment in await ListAttachmentsAsync(space.Id, ScoreRecordRole, ct).ConfigureAwait(false))
        {
            var score = await LoadScoreAsync(attachment, ct).ConfigureAwait(false);
            if (score is not null)
                await store.WriteScoreAsync(score, ct).ConfigureAwait(false);
        }

        foreach (var attachment in await ListAttachmentsAsync(space.Id, RunRecordRole, ct).ConfigureAwait(false))
        {
            var run = await LoadRunAsync(attachment, ct).ConfigureAwait(false);
            if (run is not null)
                await store.WriteRunAsync(run, ct).ConfigureAwait(false);
        }

        return store;
    }

    private async Task WriteDocumentAsync<T>(
        string spaceId,
        string role,
        string name,
        T document,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await JsonSerializer.SerializeAsync(buffer, document, JsonOptions, ct).ConfigureAwait(false);
        buffer.Position = 0;

        await _workspace.WriteContentAsync(
            _principal,
            spaceId,
            existingAttachmentId: null,
            buffer,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "application/json",
                Role = role,
                Name = name,
                Permission = "read_write",
                AttachmentMetadata = metadata
            },
            ct).ConfigureAwait(false);
    }

    private async Task<ScoreRecord?> LoadScoreAsync(
        WorkspaceContentAttachmentInfo attachment,
        CancellationToken ct)
    {
        await using var stream = await _workspace.OpenContentAsync(
            _principal,
            attachment.ContentId,
            attachment.ContentVersion,
            ct).ConfigureAwait(false);
        if (stream is null)
            return null;

        var dto = await JsonSerializer.DeserializeAsync<ScoreRecordDto>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        return dto?.ToRecord();
    }

    private async Task<EvaluationRunRecord?> LoadRunAsync(
        WorkspaceContentAttachmentInfo attachment,
        CancellationToken ct)
    {
        await using var stream = await _workspace.OpenContentAsync(
            _principal,
            attachment.ContentId,
            attachment.ContentVersion,
            ct).ConfigureAwait(false);
        if (stream is null)
            return null;

        var dto = await JsonSerializer.DeserializeAsync<RunRecordDto>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        return dto?.ToRecord();
    }

    private Task<IReadOnlyList<WorkspaceContentAttachmentInfo>> ListAttachmentsAsync(
        string spaceId,
        string role,
        CancellationToken ct)
        => _workspace.ListContentAsync(
            _principal,
            spaceId,
            new WorkspaceContentAttachmentQuery { Role = role },
            ct);

    private async Task<WorkspaceSpaceInfo> GetOrCreateResultsSpaceAsync(CancellationToken ct)
    {
        var existing = await FindResultsSpaceAsync(ct).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await _workspace.CreateSpaceAsync(
            _principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = EvaluationResultsKind,
                ExternalId = EvaluationResultsExternalId,
                Name = "Evaluation Results"
            },
            ct).ConfigureAwait(false);
    }

    private Task<WorkspaceSpaceInfo?> FindResultsSpaceAsync(CancellationToken ct)
        => _workspace.FindSpaceAsync(
            _principal,
            new WorkspaceSpaceQuery
            {
                Kind = EvaluationResultsKind,
                ExternalId = EvaluationResultsExternalId
            },
            ct);

    private static bool RunMatches(
        EvaluationRunRecord run,
        string? executionName,
        string? scenarioName,
        string? iterationName)
        => (executionName is null || run.ExecutionName == executionName) &&
           (scenarioName is null || run.ScenarioName == scenarioName) &&
           (iterationName is null || run.IterationName == iterationName);

    private static string NormalizeId(string? id)
        => string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;

    private static IReadOnlyDictionary<string, string> ScoreMetadata(ScoreRecord record)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["document_type"] = "score_record",
            ["score_id"] = NormalizeId(record.Id),
            ["evaluator"] = record.EvaluatorName,
            ["evaluator_version"] = record.EvaluatorVersion,
            ["session_id"] = record.SessionId,
            ["branch_id"] = record.BranchId,
            ["agent_name"] = record.AgentName,
            ["turn_index"] = record.TurnIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["created_at"] = record.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };

        AddIfPresent(values, "dataset_id", record.DatasetId);
        AddIfPresent(values, "dataset_version", record.DatasetVersion);
        AddIfPresent(values, "case_id", record.CaseId);
        AddIfPresent(values, "case_version", record.CaseVersion);
        AddIfPresent(values, "red_team_plugin_id", record.RedTeamPluginId);
        AddIfPresent(values, "red_team_strategy_id", record.RedTeamStrategyId);
        return values;
    }

    private static IReadOnlyDictionary<string, string> RunMetadata(EvaluationRunRecord record)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["document_type"] = "eval_run_record",
            ["run_id"] = NormalizeId(record.Id),
            ["execution_name"] = record.ExecutionName,
            ["scenario_name"] = record.ScenarioName,
            ["iteration_name"] = record.IterationName,
            ["session_id"] = record.SessionId,
            ["branch_id"] = record.BranchId,
            ["agent_name"] = record.AgentName,
            ["turn_index"] = record.TurnIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["created_at"] = record.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };

        return values;
    }

    private static void AddIfPresent(Dictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values[key] = value;
    }

    private sealed record ScoreRecordDto
    {
        public string Id { get; init; } = string.Empty;
        public string EvaluatorName { get; init; } = string.Empty;
        public string EvaluatorVersion { get; init; } = string.Empty;
        public EvaluationResultDto Result { get; init; } = new();
        public EvaluationSource Source { get; init; }
        public string SessionId { get; init; } = string.Empty;
        public string BranchId { get; init; } = string.Empty;
        public int TurnIndex { get; init; }
        public string AgentName { get; init; } = string.Empty;
        public string? ProviderKey { get; init; }
        public string? ModelId { get; init; }
        public string? ResponseModelId { get; init; }
        public string? DatasetId { get; init; }
        public string? DatasetVersion { get; init; }
        public string? CaseId { get; init; }
        public string? CaseVersion { get; init; }
        public DateTimeOffset? CaseValidFrom { get; init; }
        public DateTimeOffset? CaseValidTo { get; init; }
        public string? RedTeamPluginId { get; init; }
        public string? RedTeamStrategyId { get; init; }
        public string? RedTeamCategory { get; init; }
        public string? RedTeamSeverity { get; init; }
        public string? AttackGoal { get; init; }
        public bool? AttackSucceeded { get; init; }
        public TimeSpan TurnDuration { get; init; }
        public IReadOnlyDictionary<string, double>? Metrics { get; init; }
        public string? JudgeModelId { get; init; }
        public TimeSpan? JudgeDuration { get; init; }
        public double SamplingRate { get; init; }
        public EvalPolicy Policy { get; init; }
        public DateTimeOffset CreatedAt { get; init; }

        public static ScoreRecordDto From(ScoreRecord record)
            => new()
            {
                Id = NormalizeId(record.Id),
                EvaluatorName = record.EvaluatorName,
                EvaluatorVersion = record.EvaluatorVersion,
                Result = EvaluationResultDto.From(record.Result),
                Source = record.Source,
                SessionId = record.SessionId,
                BranchId = record.BranchId,
                TurnIndex = record.TurnIndex,
                AgentName = record.AgentName,
                ProviderKey = record.ProviderKey,
                ModelId = record.ModelId,
                ResponseModelId = record.ResponseModelId,
                DatasetId = record.DatasetId,
                DatasetVersion = record.DatasetVersion,
                CaseId = record.CaseId,
                CaseVersion = record.CaseVersion,
                CaseValidFrom = record.CaseValidFrom,
                CaseValidTo = record.CaseValidTo,
                RedTeamPluginId = record.RedTeamPluginId,
                RedTeamStrategyId = record.RedTeamStrategyId,
                RedTeamCategory = record.RedTeamCategory,
                RedTeamSeverity = record.RedTeamSeverity,
                AttackGoal = record.AttackGoal,
                AttackSucceeded = record.AttackSucceeded,
                TurnDuration = record.TurnDuration,
                Metrics = record.Metrics,
                JudgeModelId = record.JudgeModelId,
                JudgeDuration = record.JudgeDuration,
                SamplingRate = record.SamplingRate,
                Policy = record.Policy,
                CreatedAt = record.CreatedAt
            };

        public ScoreRecord ToRecord()
            => new()
            {
                Id = Id,
                EvaluatorName = EvaluatorName,
                EvaluatorVersion = EvaluatorVersion,
                Result = Result.ToResult(),
                Source = Source,
                SessionId = SessionId,
                BranchId = BranchId,
                TurnIndex = TurnIndex,
                AgentName = AgentName,
                ProviderKey = ProviderKey,
                ModelId = ModelId,
                ResponseModelId = ResponseModelId,
                DatasetId = DatasetId,
                DatasetVersion = DatasetVersion,
                CaseId = CaseId,
                CaseVersion = CaseVersion,
                CaseValidFrom = CaseValidFrom,
                CaseValidTo = CaseValidTo,
                RedTeamPluginId = RedTeamPluginId,
                RedTeamStrategyId = RedTeamStrategyId,
                RedTeamCategory = RedTeamCategory,
                RedTeamSeverity = RedTeamSeverity,
                AttackGoal = AttackGoal,
                AttackSucceeded = AttackSucceeded,
                TurnDuration = TurnDuration,
                Metrics = Metrics,
                JudgeModelId = JudgeModelId,
                JudgeDuration = JudgeDuration,
                SamplingRate = SamplingRate,
                Policy = Policy,
                CreatedAt = CreatedAt
            };
    }

    private sealed record RunRecordDto
    {
        public string Id { get; init; } = string.Empty;
        public string ExecutionName { get; init; } = string.Empty;
        public string ScenarioName { get; init; } = string.Empty;
        public string IterationName { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public IReadOnlyList<ChatMessageDto> Messages { get; init; } = [];
        public ChatMessageDto? ModelResponse { get; init; }
        public EvaluationResultDto EvaluationResult { get; init; } = new();
        public IReadOnlyList<string> Tags { get; init; } = [];
        public EvaluationSource Source { get; init; }
        public string AgentName { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;
        public string BranchId { get; init; } = string.Empty;
        public int TurnIndex { get; init; }
        public string? ProviderKey { get; init; }
        public string? ModelId { get; init; }
        public string? ResponseModelId { get; init; }
        public string? DatasetId { get; init; }
        public string? DatasetVersion { get; init; }
        public string? CaseId { get; init; }
        public string? CaseVersion { get; init; }
        public DateTimeOffset? CaseValidFrom { get; init; }
        public DateTimeOffset? CaseValidTo { get; init; }
        public TimeSpan TaskDuration { get; init; }
        public TimeSpan EvaluatorDuration { get; init; }
        public TimeSpan TotalDuration { get; init; }

        public static RunRecordDto From(EvaluationRunRecord record)
            => new()
            {
                Id = NormalizeId(record.Id),
                ExecutionName = record.ExecutionName,
                ScenarioName = record.ScenarioName,
                IterationName = record.IterationName,
                CreatedAt = record.CreatedAt,
                Messages = record.Messages.Select(ChatMessageDto.From).ToList(),
                ModelResponse = record.ModelResponse.Messages.LastOrDefault() is { } response
                    ? ChatMessageDto.From(response)
                    : null,
                EvaluationResult = EvaluationResultDto.From(record.EvaluationResult),
                Tags = record.Tags,
                Source = record.Source,
                AgentName = record.AgentName,
                SessionId = record.SessionId,
                BranchId = record.BranchId,
                TurnIndex = record.TurnIndex,
                ProviderKey = record.ProviderKey,
                ModelId = record.ModelId,
                ResponseModelId = record.ResponseModelId,
                DatasetId = record.DatasetId,
                DatasetVersion = record.DatasetVersion,
                CaseId = record.CaseId,
                CaseVersion = record.CaseVersion,
                CaseValidFrom = record.CaseValidFrom,
                CaseValidTo = record.CaseValidTo,
                TaskDuration = record.TaskDuration,
                EvaluatorDuration = record.EvaluatorDuration,
                TotalDuration = record.TotalDuration
            };

        public EvaluationRunRecord ToRecord()
            => new()
            {
                Id = Id,
                ExecutionName = ExecutionName,
                ScenarioName = ScenarioName,
                IterationName = IterationName,
                CreatedAt = CreatedAt,
                Messages = Messages.Select(m => m.ToChatMessage()).ToList(),
                ModelResponse = new ChatResponse(ModelResponse is null
                    ? []
                    : [ModelResponse.ToChatMessage()]),
                EvaluationResult = EvaluationResult.ToResult(),
                Tags = Tags,
                Source = Source,
                AgentName = AgentName,
                SessionId = SessionId,
                BranchId = BranchId,
                TurnIndex = TurnIndex,
                ProviderKey = ProviderKey,
                ModelId = ModelId,
                ResponseModelId = ResponseModelId,
                DatasetId = DatasetId,
                DatasetVersion = DatasetVersion,
                CaseId = CaseId,
                CaseVersion = CaseVersion,
                CaseValidFrom = CaseValidFrom,
                CaseValidTo = CaseValidTo,
                TaskDuration = TaskDuration,
                EvaluatorDuration = EvaluatorDuration,
                TotalDuration = TotalDuration
            };
    }

    private sealed record EvaluationResultDto
    {
        public IReadOnlyList<EvaluationMetricDto> Metrics { get; init; } = [];

        public static EvaluationResultDto From(EvaluationResult result)
            => new()
            {
                Metrics = result.Metrics.Values.Select(EvaluationMetricDto.From).ToList()
            };

        public EvaluationResult ToResult()
            => new(Metrics.Select(metric => metric.ToMetric()).ToArray());
    }

    private sealed record EvaluationMetricDto
    {
        public required string Kind { get; init; }
        public required string Name { get; init; }
        public double? NumericValue { get; init; }
        public bool? BooleanValue { get; init; }

        public static EvaluationMetricDto From(EvaluationMetric metric)
            => metric switch
            {
                NumericMetric numeric => new()
                {
                    Kind = "numeric",
                    Name = numeric.Name,
                    NumericValue = numeric.Value
                },
                BooleanMetric boolean => new()
                {
                    Kind = "boolean",
                    Name = boolean.Name,
                    BooleanValue = boolean.Value
                },
                _ => new()
                {
                    Kind = "numeric",
                    Name = metric.Name,
                    NumericValue = null
                }
            };

        public EvaluationMetric ToMetric()
            => Kind switch
            {
                "boolean" => new BooleanMetric(Name) { Value = BooleanValue },
                _ => new NumericMetric(Name) { Value = NumericValue }
            };
    }

    private sealed record ChatMessageDto
    {
        public required string Role { get; init; }
        public string? Text { get; init; }

        public static ChatMessageDto From(ChatMessage message)
            => new()
            {
                Role = message.Role.Value,
                Text = message.Text
            };

        public ChatMessage ToChatMessage()
            => new(new ChatRole(Role), Text ?? string.Empty);
    }
}
