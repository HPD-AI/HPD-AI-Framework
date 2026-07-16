// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>Options for RetroactiveScorer.</summary>
public sealed class RetroactiveScorerOptions
{
    /// <summary>
    /// When false (default), skips turns already scored by the same evaluator+version
    /// combination. Set to true to force rescoring all turns regardless.
    /// </summary>
    public bool ForceRescore { get; init; } = false;

}

/// <summary>
/// Scores saved threads without re-running the agent. Reconstructs TurnEvaluationContext
/// from Thread.Messages (typed, lossless — no OTel reconstruction required) and runs
/// evaluators against the persisted conversation history.
///
/// Token usage will be null in retroactive contexts (not persisted to Thread).
/// This is documented behavior — retroactive scoring is for content/quality evaluation.
/// </summary>
public static class RetroactiveScorer
{
    /// <summary>
    /// Score every turn in a single thread. Returns an EvaluationReport with one
    /// ReportCase per turn.
    /// </summary>
    public static async Task<EvaluationReport> ScoreThreadAsync(
        ISessionStore sessionStore,
        string sessionId,
        string threadId,
        IReadOnlyList<IEvaluator> evaluators,
        ChatConfiguration? chatConfiguration = null,
        EvalJudgeConfig? judgeConfig = null,
        RetroactiveScorerOptions? options = null,
        IScoreStore? scoreStore = null,
        CancellationToken ct = default)
    {
        options ??= new();
        chatConfiguration = chatConfiguration is not null
            ? EvaluationExecutionHelpers.WithTracing(chatConfiguration)
            : EvaluationExecutionHelpers.BuildChatConfiguration(judgeConfig);

        var thread = await sessionStore.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.Evaluation, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"Thread '{threadId}' in session '{sessionId}' not found.", nameof(threadId));

        var cases = await ScoreThreadInternalAsync(
            sessionId, thread, evaluators, chatConfiguration, options, scoreStore, ct)
            .ConfigureAwait(false);

        return new EvaluationReport($"retroactive:{sessionId}/{threadId}", cases);
    }

    /// <summary>
    /// Score two threads and return a comparison report.
    /// </summary>
    public static async Task<ThreadComparisonReport> CompareThreadsAsync(
        ISessionStore sessionStore,
        string sessionId,
        string threadId1,
        string threadId2,
        IReadOnlyList<IEvaluator> evaluators,
        ChatConfiguration? chatConfiguration = null,
        RetroactiveScorerOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new();
        chatConfiguration = EvaluationExecutionHelpers.WithTracing(chatConfiguration);

        var thread1Task = sessionStore.ProjectThreadAsync(sessionId, threadId1, ThreadProjectionPurpose.Evaluation, ct);
        var thread2Task = sessionStore.ProjectThreadAsync(sessionId, threadId2, ThreadProjectionPurpose.Evaluation, ct);

        var thread1 = await thread1Task.ConfigureAwait(false)
            ?? throw new ArgumentException($"Thread '{threadId1}' not found.");
        var thread2 = await thread2Task.ConfigureAwait(false)
            ?? throw new ArgumentException($"Thread '{threadId2}' not found.");

        var reports = await Task.WhenAll(
            ScoreThreadInternalAsync(sessionId, thread1, evaluators, chatConfiguration, options, null, ct),
            ScoreThreadInternalAsync(sessionId, thread2, evaluators, chatConfiguration, options, null, ct)
        ).ConfigureAwait(false);

        return new ThreadComparisonReport(
            new EvaluationReport($"thread:{threadId1}", reports[0]),
            new EvaluationReport($"thread:{threadId2}", reports[1]));
    }

    /// <summary>
    /// Tournament: rank N threads by a single evaluator's score.
    /// Returns threads sorted descending by average score.
    /// </summary>
    public static async Task<TournamentResult> TournamentAsync(
        ISessionStore sessionStore,
        string sessionId,
        IReadOnlyList<string> threadIds,
        IEvaluator evaluator,
        ChatConfiguration? chatConfiguration = null,
        CancellationToken ct = default)
    {
        chatConfiguration = EvaluationExecutionHelpers.WithTracing(chatConfiguration);
        var options = new RetroactiveScorerOptions();
        var scoreTasks = threadIds.Select(async threadId =>
        {
            var thread = await sessionStore.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.Evaluation, ct).ConfigureAwait(false);
            if (thread is null) return (threadId, 0.0, 0);

            var cases = await ScoreThreadInternalAsync(
                sessionId, thread, [evaluator], chatConfiguration, options, null, ct)
                .ConfigureAwait(false);

            var report = new EvaluationReport($"thread:{threadId}", cases);
            var metricName = evaluator.EvaluationMetricNames.FirstOrDefault() ?? string.Empty;
            return (threadId, report.AverageScore(metricName), cases.Count);
        });

        var results = await Task.WhenAll(scoreTasks).ConfigureAwait(false);
        var ranked = results.OrderByDescending(r => r.Item2).ToList();

        return new TournamentResult(ranked.Select((r, rank) =>
            new TournamentEntry(r.Item1, rank + 1, r.Item2, r.Item3)).ToList());
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static async Task<List<ReportCase>> ScoreThreadInternalAsync(
        string sessionId,
        Thread thread,
        IReadOnlyList<IEvaluator> evaluators,
        ChatConfiguration? chatConfiguration,
        RetroactiveScorerOptions options,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        var agentName = thread.Session?.Metadata.TryGetValue("agentName", out var n) == true
            ? n?.ToString() ?? string.Empty
            : string.Empty;

        var turnContexts = TurnEvaluationContextBuilder.FromThread(thread, agentName);
        var cases = new List<ReportCase>();

        foreach (var turnCtx in turnContexts)
        {
            var additionalContext = new List<EvaluationContext>
            {
                new TurnEvaluationContextWrapper(turnCtx),
            };

            var messages = turnCtx.ConversationHistory
                .Append(new ChatMessage(ChatRole.User, turnCtx.UserInput))
                .ToList();

            var evalResults = new List<EvaluationResult>();
            var failures = new List<EvaluatorFailure>();

            foreach (var evaluator in evaluators)
            {
                var evaluatorName = evaluator.GetType().Name;
                var version = (evaluator as IHpdEvaluator)?.Version ?? "1.0.0";

                // Deduplication: skip turns already scored by the same evaluator+version
                // unless ForceRescore is set. Checks IScoreStore for an existing record
                // with matching (evaluatorName, evaluatorVersion, sessionId, threadId, turnIndex).
                if (!options.ForceRescore && scoreStore is not null)
                {
                    bool alreadyScored = false;
                    await foreach (var existing in scoreStore.GetScoresByVersionAsync(
                        evaluatorName, version, ct).ConfigureAwait(false))
                    {
                        if (existing.SessionId == turnCtx.SessionId &&
                            existing.ThreadId == turnCtx.ThreadId &&
                            existing.TurnIndex == turnCtx.TurnIndex)
                        {
                            alreadyScored = true;
                            break;
                        }
                    }

                    if (alreadyScored)
                        continue;
                }

                try
                {
                    using var traceScope = EvalTraceContext.Activate(evaluatorName);
                    var evalResult = await evaluator.EvaluateAsync(
                        messages, turnCtx.FinalResponse, chatConfiguration,
                        additionalContext, ct).ConfigureAwait(false);
                    var judgeCalls = traceScope.Snapshot();

                    evalResults.Add(evalResult);

                    if (scoreStore is not null)
                    {
                        await scoreStore.WriteScoreAsync(new ScoreRecord
                        {
                            Id = Guid.NewGuid().ToString(),
                            EvaluatorName = evaluatorName,
                            EvaluatorVersion = version,
                            Result = evalResult,
                            Source = EvaluationSource.Retroactive,
                            SessionId = turnCtx.SessionId,
                            ThreadId = turnCtx.ThreadId,
                            TurnIndex = turnCtx.TurnIndex,
                            AgentName = turnCtx.AgentName,
                            ProviderKey = turnCtx.ProviderKey,
                            ModelId = turnCtx.ModelId,
                            ResponseModelId = turnCtx.ResponseModelId,
                            JudgeCalls = judgeCalls,
                            CreatedAt = DateTimeOffset.UtcNow,
                        }, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new EvaluatorFailure(evaluatorName, ex.Message));
                }
            }

            // Merge all results into one EvaluationResult for the report case
            var mergedResult = evalResults.Count > 0
                ? MergeResults(evalResults)
                : new EvaluationResult();

            cases.Add(new ReportCase(
                Name: $"turn-{turnCtx.TurnIndex}",
                ProviderKey: turnCtx.ProviderKey,
                ModelId: turnCtx.ModelId,
                ResponseModelId: turnCtx.ResponseModelId,
                EvaluationResult: mergedResult,
                EvaluatorFailures: failures,
                TaskDuration: turnCtx.Duration,
                EvaluatorDuration: TimeSpan.Zero,
                TotalDuration: turnCtx.Duration));
        }

        return cases;
    }

    private static EvaluationResult MergeResults(List<EvaluationResult> results)
    {
        var merged = new EvaluationResult();
        foreach (var result in results)
        foreach (var (name, metric) in result.Metrics)
            merged.Metrics[name] = metric;
        return merged;
    }
}

/// <summary>Comparison of two thread evaluation reports.</summary>
public sealed record ThreadComparisonReport(
    EvaluationReport Thread1Report,
    EvaluationReport Thread2Report);

/// <summary>Ranked results from a tournament evaluation.</summary>
public sealed record TournamentResult(IReadOnlyList<TournamentEntry> Rankings);

public sealed record TournamentEntry(
    string ThreadId,
    int Rank,
    double AverageScore,
    int TurnCount);
