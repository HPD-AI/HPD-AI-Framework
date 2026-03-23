// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

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
/// Scores saved branches without re-running the agent. Reconstructs TurnEvaluationContext
/// from Branch.Messages (typed, lossless — no OTel reconstruction required) and runs
/// evaluators against the persisted conversation history.
///
/// Token usage will be null in retroactive contexts (not persisted to Branch).
/// This is documented behavior — retroactive scoring is for content/quality evaluation.
/// </summary>
public static class RetroactiveScorer
{
    /// <summary>
    /// Score every turn in a single branch. Returns an EvaluationReport with one
    /// ReportCase per turn.
    /// </summary>
    public static async Task<EvaluationReport> ScoreBranchAsync(
        ISessionStore sessionStore,
        string sessionId,
        string branchId,
        IReadOnlyList<IEvaluator> evaluators,
        ChatConfiguration? chatConfiguration = null,
        EvalJudgeConfig? judgeConfig = null,
        RetroactiveScorerOptions? options = null,
        IScoreStore? scoreStore = null,
        CancellationToken ct = default)
    {
        options ??= new();

        var branch = await sessionStore.LoadBranchAsync(sessionId, branchId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"Branch '{branchId}' in session '{sessionId}' not found.", nameof(branchId));

        var cases = await ScoreBranchInternalAsync(
            sessionId, branch, evaluators, chatConfiguration, options, scoreStore, ct)
            .ConfigureAwait(false);

        return new EvaluationReport($"retroactive:{sessionId}/{branchId}", cases);
    }

    /// <summary>
    /// Score two branches and return a comparison report.
    /// </summary>
    public static async Task<BranchComparisonReport> CompareBranchesAsync(
        ISessionStore sessionStore,
        string sessionId,
        string branchId1,
        string branchId2,
        IReadOnlyList<IEvaluator> evaluators,
        ChatConfiguration? chatConfiguration = null,
        RetroactiveScorerOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new();

        var branch1Task = sessionStore.LoadBranchAsync(sessionId, branchId1, ct);
        var branch2Task = sessionStore.LoadBranchAsync(sessionId, branchId2, ct);

        var branch1 = await branch1Task.ConfigureAwait(false)
            ?? throw new ArgumentException($"Branch '{branchId1}' not found.");
        var branch2 = await branch2Task.ConfigureAwait(false)
            ?? throw new ArgumentException($"Branch '{branchId2}' not found.");

        var reports = await Task.WhenAll(
            ScoreBranchInternalAsync(sessionId, branch1, evaluators, chatConfiguration, options, null, ct),
            ScoreBranchInternalAsync(sessionId, branch2, evaluators, chatConfiguration, options, null, ct)
        ).ConfigureAwait(false);

        return new BranchComparisonReport(
            new EvaluationReport($"branch:{branchId1}", reports[0]),
            new EvaluationReport($"branch:{branchId2}", reports[1]));
    }

    /// <summary>
    /// Tournament: rank N branches by a single evaluator's score.
    /// Returns branches sorted descending by average score.
    /// </summary>
    public static async Task<TournamentResult> TournamentAsync(
        ISessionStore sessionStore,
        string sessionId,
        IReadOnlyList<string> branchIds,
        IEvaluator evaluator,
        ChatConfiguration? chatConfiguration = null,
        CancellationToken ct = default)
    {
        var options = new RetroactiveScorerOptions();
        var scoreTasks = branchIds.Select(async branchId =>
        {
            var branch = await sessionStore.LoadBranchAsync(sessionId, branchId, ct).ConfigureAwait(false);
            if (branch is null) return (branchId, 0.0, 0);

            var cases = await ScoreBranchInternalAsync(
                sessionId, branch, [evaluator], chatConfiguration, options, null, ct)
                .ConfigureAwait(false);

            var report = new EvaluationReport($"branch:{branchId}", cases);
            var metricName = evaluator.EvaluationMetricNames.FirstOrDefault() ?? string.Empty;
            return (branchId, report.AverageScore(metricName), cases.Count);
        });

        var results = await Task.WhenAll(scoreTasks).ConfigureAwait(false);
        var ranked = results.OrderByDescending(r => r.Item2).ToList();

        return new TournamentResult(ranked.Select((r, rank) =>
            new TournamentEntry(r.Item1, rank + 1, r.Item2, r.Item3)).ToList());
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static async Task<List<ReportCase>> ScoreBranchInternalAsync(
        string sessionId,
        Branch branch,
        IReadOnlyList<IEvaluator> evaluators,
        ChatConfiguration? chatConfiguration,
        RetroactiveScorerOptions options,
        IScoreStore? scoreStore,
        CancellationToken ct)
    {
        var agentName = branch.Session?.Metadata.TryGetValue("agentName", out var n) == true
            ? n?.ToString() ?? string.Empty
            : string.Empty;

        var turnContexts = TurnEvaluationContextBuilder.FromBranch(branch, agentName);
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
                // with matching (evaluatorName, evaluatorVersion, sessionId, branchId, turnIndex).
                if (!options.ForceRescore && scoreStore is not null)
                {
                    bool alreadyScored = false;
                    await foreach (var existing in scoreStore.GetScoresByVersionAsync(
                        evaluatorName, version, ct).ConfigureAwait(false))
                    {
                        if (existing.SessionId == turnCtx.SessionId &&
                            existing.BranchId == turnCtx.BranchId &&
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
                    var evalResult = await evaluator.EvaluateAsync(
                        messages, turnCtx.FinalResponse, chatConfiguration,
                        additionalContext, ct).ConfigureAwait(false);

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
                            BranchId = turnCtx.BranchId,
                            TurnIndex = turnCtx.TurnIndex,
                            AgentName = turnCtx.AgentName,
                            ModelId = turnCtx.ModelId,
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

/// <summary>Comparison of two branch evaluation reports.</summary>
public sealed record BranchComparisonReport(
    EvaluationReport Branch1Report,
    EvaluationReport Branch2Report);

/// <summary>Ranked results from a tournament evaluation.</summary>
public sealed record TournamentResult(IReadOnlyList<TournamentEntry> Rankings);

public sealed record TournamentEntry(
    string BranchId,
    int Rank,
    double AverageScore,
    int TurnCount);
