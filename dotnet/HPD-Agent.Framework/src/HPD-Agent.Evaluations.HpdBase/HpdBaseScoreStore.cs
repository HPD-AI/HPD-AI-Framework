// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Evaluations.Storage;
using HPD.Base;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.HpdBase;

/// <summary>Durable HPD Base implementation of evaluation score and run storage.</summary>
public sealed class HpdBaseScoreStore : IScoreStore
{
    private const int PageSize = 250;
    private static readonly PrincipalContext StorePrincipal = new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "hpd-agent-stores",
    };

    private readonly BaseSession _session;

    /// <summary>Creates an HPD Base-backed evaluation store.</summary>
    public HpdBaseScoreStore(IBaseSessionFactory sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _session = sessions.For(StorePrincipal);
    }

    /// <inheritdoc />
    public async ValueTask WriteScoreAsync(
        ScoreRecord record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EvaluationMetric? metric = record.Result.Metrics.Values.FirstOrDefault();
        HpdBaseEvaluationScore stored = new()
        {
            EvaluatorName = record.EvaluatorName,
            EvaluatorVersion = record.EvaluatorVersion,
            SessionId = record.SessionId,
            ThreadId = record.ThreadId,
            TurnIndex = record.TurnIndex,
            AgentName = record.AgentName,
            Source = record.Source.ToString(),
            Policy = record.Policy.ToString(),
            PrimaryScore = PrimaryScore(metric),
            Passed = IsPassing(record.Result),
            ProviderKey = record.ProviderKey,
            ModelId = record.ModelId,
            DatasetId = record.DatasetId,
            RedTeamPluginId = record.RedTeamPluginId,
            RedTeamStrategyId = record.RedTeamStrategyId,
            AttackSucceeded = record.AttackSucceeded,
            CreatedAt = record.CreatedAt,
            Payload = JsonSerializer.Serialize(
                record,
                HpdBaseEvaluationStoreJsonContext.Default.ScoreRecord),
        };

        await _session.Collection(HpdBaseEvaluationScore.Collection)
            .UpsertAsync(
                RecordId.Create(RecordKey("score", record.Id)),
                stored,
                stored,
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask WriteRunAsync(
        EvaluationRunRecord record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        HpdBaseEvaluationRun stored = new()
        {
            ExecutionName = record.ExecutionName,
            ScenarioName = record.ScenarioName,
            IterationName = record.IterationName,
            CreatedAt = record.CreatedAt,
            Payload = JsonSerializer.Serialize(
                record,
                HpdBaseEvaluationStoreJsonContext.Default.EvaluationRunRecord),
        };

        await _session.Collection(HpdBaseEvaluationRun.Collection)
            .UpsertAsync(
                RecordId.Create(RecordKey("run", record.Id)),
                stored,
                stored,
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string sessionId,
        string? threadId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return QueryScoresAsync(
            item => item.SessionId == sessionId &&
                    (threadId is null || item.ThreadId == threadId),
            ct);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ScoreRecord> GetScoresAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluatorName);
        return QueryScoresAsync(
            item => item.EvaluatorName == evaluatorName &&
                    (from is null || item.CreatedAt >= from) &&
                    (to is null || item.CreatedAt <= to),
            ct);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<EvaluationRunRecord> GetRunsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        CancellationToken ct = default)
        => QueryRunsAsync(
            item => (executionName is null || item.ExecutionName == executionName) &&
                    (scenarioName is null || item.ScenarioName == scenarioName) &&
                    (iterationName is null || item.IterationName == iterationName),
            ct);

    /// <inheritdoc />
    public async ValueTask DeleteRunsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        CancellationToken ct = default)
    {
        var matches = new List<BaseRecord<HpdBaseEvaluationRun>>();
        await foreach (BaseRecord<HpdBaseEvaluationRun> item in QueryRunRecordsAsync(
                           run => (executionName is null || run.ExecutionName == executionName) &&
                                  (scenarioName is null || run.ScenarioName == scenarioName) &&
                                  (iterationName is null || run.IterationName == iterationName),
                           ct).ConfigureAwait(false))
            matches.Add(item);

        BaseCollectionSession<HpdBaseEvaluationRun> collection =
            _session.Collection(HpdBaseEvaluationRun.Collection);
        foreach (BaseRecord<HpdBaseEvaluationRun> item in matches)
            await collection.DeleteAsync(item.Id, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GetLatestRunExecutionNamesAsync(
        int? count = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<string> names = await LoadRunExecutionNamesAsync(ct).ConfigureAwait(false);
        foreach (string name in names.Take(count ?? int.MaxValue))
            yield return name;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GetRunScenarioNamesAsync(
        string executionName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionName);
        var names = new HashSet<string>(StringComparer.Ordinal);
        await foreach (EvaluationRunRecord run in GetRunsAsync(executionName, ct: ct)
                           .ConfigureAwait(false))
            names.Add(run.ScenarioName);
        foreach (string name in names.Order(StringComparer.Ordinal))
            yield return name;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GetRunIterationNamesAsync(
        string executionName,
        string scenarioName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await foreach (EvaluationRunRecord run in GetRunsAsync(
                           executionName, scenarioName, ct: ct).ConfigureAwait(false))
            names.Add(run.IterationName);
        foreach (string name in names.Order(StringComparer.Ordinal))
            yield return name;
    }

    /// <inheritdoc />
    public async ValueTask<ScoreTrend> GetTrendAsync(
        string evaluatorName,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan bucketSize,
        CancellationToken ct = default)
        => await SnapshotAsync(evaluatorName, from, to, ct)
            .ContinueWithAsync(store => store.GetTrendAsync(evaluatorName, from, to, bucketSize, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<double> GetPassRateAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(evaluatorName, from, to, ct)
            .ContinueWithAsync(store => store.GetPassRateAsync(evaluatorName, from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, ScoreAggregate>> GetAgentComparisonAsync(
        string evaluatorName,
        IEnumerable<string> agentNames,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(evaluatorName, from, to, ct)
            .ContinueWithAsync(store => store.GetAgentComparisonAsync(
                evaluatorName, agentNames, from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<ThreadComparisonResult> GetThreadComparisonAsync(
        string sessionId,
        string threadId1,
        string threadId2,
        IEnumerable<string> evaluatorNames,
        CancellationToken ct = default)
    {
        InMemoryScoreStore snapshot = new();
        await LoadIntoSnapshotAsync(item => item.SessionId == sessionId, snapshot, ct)
            .ConfigureAwait(false);
        return await snapshot.GetThreadComparisonAsync(
            sessionId, threadId1, threadId2, evaluatorNames, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvaluatorSummary>> GetEvaluatorSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetEvaluatorSummaryAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<double> GetFailureRateAsync(
        string evaluatorName,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(evaluatorName, from, to, ct)
            .ContinueWithAsync(store => store.GetFailureRateAsync(evaluatorName, from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, double>> GetCostBreakdownAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetCostBreakdownAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public IAsyncEnumerable<ScoreRecord> GetScoresByVersionAsync(
        string evaluatorName,
        string version,
        CancellationToken ct = default)
        => QueryScoresAsync(
            item => item.EvaluatorName == evaluatorName &&
                    item.EvaluatorVersion == version,
            ct);

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, ToolUsageSummary>> GetToolUsageSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetToolUsageSummaryAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RiskAutonomyDataPoint>> GetRiskAutonomyDistributionAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetRiskAutonomyDistributionAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<double> GetAttackSuccessRateAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetAttackSuccessRateAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, double>> GetAttackSuccessRateByPluginAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetAttackSuccessRateByPluginAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, double>> GetAttackSuccessRateByStrategyAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetAttackSuccessRateByStrategyAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RedTeamFinding>> GetRedTeamFindingsAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
        => await SnapshotAsync(null, from, to, ct)
            .ContinueWithAsync(store => store.GetRedTeamFindingsAsync(from, to, ct))
            .ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask DeleteResultsAsync(
        string? executionName,
        string? scenarioName,
        string? iterationName,
        CancellationToken ct = default)
        => DeleteRunsAsync(executionName, scenarioName, iterationName, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<string> GetLatestExecutionNamesAsync(
        int? maxCount = null,
        CancellationToken ct = default)
        => GetLatestRunExecutionNamesAsync(maxCount, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<string> GetScenarioNamesAsync(
        string? executionName,
        CancellationToken ct = default)
        => QueryRunScenarioNamesAsync(executionName, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<string> GetIterationNamesAsync(
        string? executionName,
        string? scenarioName,
        CancellationToken ct = default)
        => QueryRunIterationNamesAsync(executionName, scenarioName, ct);

    /// <inheritdoc />
    public async IAsyncEnumerable<ScenarioRunResult> ReadResultsAsync(
        string? executionName,
        string? scenarioName,
        string? iterationName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (EvaluationRunRecord run in GetRunsAsync(
                           executionName, scenarioName, iterationName, ct).ConfigureAwait(false))
            yield return run.ToScenarioRunResult();
    }

    /// <inheritdoc />
    public async ValueTask WriteResultsAsync(
        IEnumerable<ScenarioRunResult> results,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        foreach (ScenarioRunResult result in results)
            await WriteRunAsync(EvaluationRunRecord.FromScenarioRunResult(result), ct)
                .ConfigureAwait(false);
    }

    private async IAsyncEnumerable<ScoreRecord> QueryScoresAsync(
        Func<HpdBaseEvaluationScore, bool> predicate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (HpdBaseEvaluationScore item in QueryScoreItemsAsync(predicate, ct)
                           .ConfigureAwait(false))
            yield return DeserializeScore(item);
    }

    private async IAsyncEnumerable<EvaluationRunRecord> QueryRunsAsync(
        Func<HpdBaseEvaluationRun, bool> predicate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (HpdBaseEvaluationRun item in QueryRunItemsAsync(predicate, ct)
                           .ConfigureAwait(false))
            yield return DeserializeRun(item);
    }

    private async IAsyncEnumerable<HpdBaseEvaluationScore> QueryScoreItemsAsync(
        Func<HpdBaseEvaluationScore, bool> predicate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        BaseQuery<HpdBaseEvaluationScore> query = _session
            .Collection(HpdBaseEvaluationScore.Collection)
            .Query()
            .OrderBy(HpdBaseEvaluationScore.Fields.CreatedAt)
            .Take(PageSize);

        while (true)
        {
            BasePage<BaseRecord<HpdBaseEvaluationScore>> page =
                (await query.PageAsync(ct).ConfigureAwait(false)).RequireValue();
            foreach (BaseRecord<HpdBaseEvaluationScore> item in page.Items)
                if (predicate(item.Value))
                    yield return item.Value;

            if (!page.Page.HasMore || string.IsNullOrWhiteSpace(page.Page.NextCursor))
                yield break;
            query = query.ContinueFrom(page.Page.NextCursor);
        }
    }

    private async IAsyncEnumerable<HpdBaseEvaluationRun> QueryRunItemsAsync(
        Func<HpdBaseEvaluationRun, bool> predicate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        BaseQuery<HpdBaseEvaluationRun> query = _session
            .Collection(HpdBaseEvaluationRun.Collection)
            .Query()
            .OrderBy(HpdBaseEvaluationRun.Fields.CreatedAt)
            .Take(PageSize);

        while (true)
        {
            BasePage<BaseRecord<HpdBaseEvaluationRun>> page =
                (await query.PageAsync(ct).ConfigureAwait(false)).RequireValue();
            foreach (BaseRecord<HpdBaseEvaluationRun> item in page.Items)
                if (predicate(item.Value))
                    yield return item.Value;

            if (!page.Page.HasMore || string.IsNullOrWhiteSpace(page.Page.NextCursor))
                yield break;
            query = query.ContinueFrom(page.Page.NextCursor);
        }
    }

    private async IAsyncEnumerable<BaseRecord<HpdBaseEvaluationRun>> QueryRunRecordsAsync(
        Func<HpdBaseEvaluationRun, bool> predicate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        BaseQuery<HpdBaseEvaluationRun> query = _session
            .Collection(HpdBaseEvaluationRun.Collection)
            .Query()
            .OrderBy(HpdBaseEvaluationRun.Fields.CreatedAt)
            .Take(PageSize);

        while (true)
        {
            BasePage<BaseRecord<HpdBaseEvaluationRun>> page =
                (await query.PageAsync(ct).ConfigureAwait(false)).RequireValue();
            foreach (BaseRecord<HpdBaseEvaluationRun> item in page.Items)
                if (predicate(item.Value))
                    yield return item;

            if (!page.Page.HasMore || string.IsNullOrWhiteSpace(page.Page.NextCursor))
                yield break;
            query = query.ContinueFrom(page.Page.NextCursor);
        }
    }

    private async IAsyncEnumerable<string> QueryRunScenarioNamesAsync(
        string? executionName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await foreach (HpdBaseEvaluationRun run in QueryRunItemsAsync(
                           item => executionName is null || item.ExecutionName == executionName,
                           ct).ConfigureAwait(false))
            names.Add(run.ScenarioName);
        foreach (string name in names.Order(StringComparer.Ordinal))
            yield return name;
    }

    private async IAsyncEnumerable<string> QueryRunIterationNamesAsync(
        string? executionName,
        string? scenarioName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await foreach (HpdBaseEvaluationRun run in QueryRunItemsAsync(
                           item => (executionName is null || item.ExecutionName == executionName) &&
                                   (scenarioName is null || item.ScenarioName == scenarioName),
                           ct).ConfigureAwait(false))
            names.Add(run.IterationName);
        foreach (string name in names.Order(StringComparer.Ordinal))
            yield return name;
    }

    private async Task<List<string>> LoadRunExecutionNamesAsync(CancellationToken ct)
    {
        var names = new List<string>();
        await foreach (HpdBaseEvaluationRun run in QueryRunItemsAsync(_ => true, ct)
                           .ConfigureAwait(false))
            names.Add(run.ExecutionName);
        return names.Distinct(StringComparer.Ordinal).Reverse().ToList();
    }

    private async Task<InMemoryScoreStore> SnapshotAsync(
        string? evaluatorName,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        InMemoryScoreStore snapshot = new();
        await LoadIntoSnapshotAsync(
            item => (evaluatorName is null || item.EvaluatorName == evaluatorName) &&
                    (from is null || item.CreatedAt >= from) &&
                    (to is null || item.CreatedAt <= to),
            snapshot,
            ct).ConfigureAwait(false);
        return snapshot;
    }

    private async Task LoadIntoSnapshotAsync(
        Func<HpdBaseEvaluationScore, bool> predicate,
        InMemoryScoreStore snapshot,
        CancellationToken ct)
    {
        await foreach (HpdBaseEvaluationScore item in QueryScoreItemsAsync(predicate, ct)
                           .ConfigureAwait(false))
            await snapshot.WriteScoreAsync(DeserializeScore(item), ct).ConfigureAwait(false);
    }

    private static ScoreRecord DeserializeScore(HpdBaseEvaluationScore item)
    {
        try
        {
            return JsonSerializer.Deserialize(
                       item.Payload,
                       HpdBaseEvaluationStoreJsonContext.Default.ScoreRecord)
                   ?? throw new JsonException("Score payload was JSON null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Stored evaluation score '{item.EvaluatorName}' contains invalid JSON.",
                ex);
        }
    }

    private static EvaluationRunRecord DeserializeRun(HpdBaseEvaluationRun item)
    {
        try
        {
            return JsonSerializer.Deserialize(
                       item.Payload,
                       HpdBaseEvaluationStoreJsonContext.Default.EvaluationRunRecord)
                   ?? throw new JsonException("Run payload was JSON null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Stored evaluation run '{item.ExecutionName}/{item.ScenarioName}/{item.IterationName}' contains invalid JSON.",
                ex);
        }
    }

    private static bool IsPassing(EvaluationResult result)
    {
        if (result.Metrics.Count == 0)
            return false;
        foreach (EvaluationMetric metric in result.Metrics.Values)
        {
            if (metric.Interpretation is { } interpretation)
            {
                if (interpretation.Failed)
                    return false;
                continue;
            }

            bool passed = metric switch
            {
                BooleanMetric boolean => boolean.Value == true,
                NumericMetric numeric => numeric.Value is { } value &&
                                         double.IsFinite(value) &&
                                         value >= 0.5,
                _ => false,
            };
            if (!passed)
                return false;
        }
        return true;
    }

    private static double? PrimaryScore(EvaluationMetric? metric) => metric switch
    {
        NumericMetric numeric => numeric.Value,
        BooleanMetric boolean => boolean.Value.HasValue
            ? (boolean.Value.Value ? 1.0 : 0.0)
            : null,
        _ => null,
    };

    private static string RecordKey(string kind, string id) =>
        kind + "_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(id)))
            .ToLowerInvariant();
}

internal static class TaskExtensions
{
    internal static async Task<TResult> ContinueWithAsync<T, TResult>(
        this Task<T> task,
        Func<T, Task<TResult>> continuation)
    {
        T value = await task.ConfigureAwait(false);
        return await continuation(value).ConfigureAwait(false);
    }

    internal static async Task<TResult> ContinueWithAsync<T, TResult>(
        this Task<T> task,
        Func<T, ValueTask<TResult>> continuation)
    {
        T value = await task.ConfigureAwait(false);
        return await continuation(value).ConfigureAwait(false);
    }
}
