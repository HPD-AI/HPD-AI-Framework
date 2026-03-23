// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Middleware;

namespace HPD.Agent.Evaluations.Evaluators.LlmJudge;

// ── ConversationEvalStateData ────────────────────────────────────────────────

/// <summary>
/// Branch-scoped persistent middleware state for conversation-level evaluators.
/// Accumulates facts across turns so evaluators can assess coherence and memory.
/// Written by EvaluationMiddleware.AfterMessageTurnAsync via UpdateMiddlewareState.
/// </summary>
[MiddlewareState(Persistent = true, Scope = StateScope.Branch)]
public sealed record ConversationEvalStateData
{
    /// <summary>Goal established in the first user turn (if detectable).</summary>
    public string? EstablishedGoal { get; init; }

    /// <summary>Key facts stated by the agent across all prior turns.</summary>
    public IReadOnlyList<string> EstablishedFacts { get; init; } = [];

    /// <summary>Agent response texts from prior turns (truncated for space).</summary>
    public IReadOnlyList<string> PriorResponses { get; init; } = [];

    /// <summary>Number of turns completed in this branch so far.</summary>
    public int TurnCount { get; init; }
}

// ── ConversationCoherenceEvaluator ───────────────────────────────────────────

/// <summary>
/// LLM-as-judge: does the agent maintain consistent context and tone across turns? (0–1)
/// Requires ConversationHistoryContext. Default policy: TrackTrend.
/// </summary>
public sealed class ConversationCoherenceEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Conversation Coherence";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var historyCtx = additionalContext?.OfType<ConversationHistoryContext>().FirstOrDefault();
        var history = historyCtx?.History ?? [];
        var historyText = history.Count > 0
            ? string.Join("\n", history.Select(m => $"{m.Role.Value}: {m.Text}"))
            : "(no prior turns)";

        return
        [
            new(ChatRole.System,
                "Rate how coherently the current response continues the conversation. " +
                "Score 0–1: 1 = perfectly consistent with prior context, persona, and facts. " +
                "0 = contradicts or ignores established context. " +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Prior conversation:\n{historyText}\n\n" +
                $"Current response:\n{modelResponse.Text}"),
        ];
    }

    protected override void ParseJudgeResponse(
        string responseText, EvaluationResult result, ChatResponse judgeResponse, TimeSpan duration)
    {
        var (_, reason, score) = TagParser.Parse(responseText);
        var metric = (NumericMetric)result.Metrics.Values.First();
        if (TagParser.TryParseDouble(score, out var v))
            metric.Value = Math.Clamp(v, 0.0, 1.0);
        metric.Reason = reason;
        metric.AddOrUpdateChatMetadata(judgeResponse, duration);
    }
}

// ── GoalProgressionEvaluator ─────────────────────────────────────────────────

/// <summary>
/// JSON-judge: is the agent making measurable progress toward the user's goal? (0–1)
/// Uses JSON output to capture both the score and structured metadata (goal statement,
/// progression reasoning, blockers). Default policy: TrackTrend.
/// </summary>
public sealed class GoalProgressionEvaluator : HpdJsonJudgeEvaluatorBase<GoalProgressionRating>
{
    public const string MetricName = "Goal Progression";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var historyCtx = additionalContext?.OfType<ConversationHistoryContext>().FirstOrDefault();
        var history = historyCtx?.History ?? [];
        var historyText = history.Count > 0
            ? string.Join("\n", history.Take(10).Select(m => $"{m.Role.Value}: {m.Text?[..Math.Min(200, m.Text?.Length ?? 0)]}"))
            : "(no prior turns)";

        return
        [
            new(ChatRole.System,
                "Evaluate goal progression in this conversation. Return JSON: " +
                "{\"score\": 0.0-1.0, \"goal\": \"...\", \"progression_reason\": \"...\", \"blockers\": []}. " +
                "score=1 means the goal is fully achieved this turn; score=0 means no progress."),
            new(ChatRole.User,
                $"Conversation so far:\n{historyText}\n\n" +
                $"Current response:\n{modelResponse.Text}"),
        ];
    }

    protected override GoalProgressionRating? ParseRating(string json)
    {
        try { return JsonSerializer.Deserialize<GoalProgressionRating>(json); }
        catch { return null; }
    }

    protected override void PopulateResult(
        GoalProgressionRating rating, EvaluationResult result,
        ChatResponse judgeResponse, TimeSpan duration)
    {
        var metric = (NumericMetric)result.Metrics.Values.First();
        metric.Value = Math.Clamp(rating.Score, 0.0, 1.0);
        metric.Reason = rating.ProgressionReason;
        if (rating.Goal is not null)
            metric.AddOrUpdateMetadata("goal", rating.Goal);
        if (rating.Blockers?.Length > 0)
            metric.AddOrUpdateMetadata("blockers", string.Join("; ", rating.Blockers));
        metric.AddOrUpdateChatMetadata(judgeResponse, duration);
    }
}

public sealed class GoalProgressionRating
{
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("goal")] public string? Goal { get; set; }
    [JsonPropertyName("progression_reason")] public string? ProgressionReason { get; set; }
    [JsonPropertyName("blockers")] public string[]? Blockers { get; set; }
}

// ── RepetitionDetectionEvaluator ─────────────────────────────────────────────

/// <summary>
/// LLM-as-judge: is the agent repeating itself across turns? Score 0–1 where
/// lower = more repetitive (worse). Default policy: TrackTrend.
/// </summary>
public sealed class RepetitionDetectionEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Repetition";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var historyCtx = additionalContext?.OfType<ConversationHistoryContext>().FirstOrDefault();
        var priorResponses = historyCtx?.History
            .Where(m => m.Role == ChatRole.Assistant)
            .Select(m => m.Text ?? string.Empty)
            .ToList() ?? [];

        var priorText = priorResponses.Count > 0
            ? string.Join("\n---\n", priorResponses.TakeLast(5).Select((r, i) => $"Turn {i + 1}: {r}"))
            : "(no prior responses)";

        return
        [
            new(ChatRole.System,
                "Rate how much the current response repeats information already given in prior responses. " +
                "Score 0–1: 0 = completely repetitive, 1 = entirely novel content. " +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Prior responses:\n{priorText}\n\nCurrent response:\n{modelResponse.Text}"),
        ];
    }

    protected override void ParseJudgeResponse(
        string responseText, EvaluationResult result, ChatResponse judgeResponse, TimeSpan duration)
    {
        var (_, reason, score) = TagParser.Parse(responseText);
        var metric = (NumericMetric)result.Metrics.Values.First();
        if (TagParser.TryParseDouble(score, out var v))
            metric.Value = Math.Clamp(v, 0.0, 1.0);
        metric.Reason = reason;
        metric.AddOrUpdateChatMetadata(judgeResponse, duration);
    }
}

// ── MemoryAccuracyEvaluator ──────────────────────────────────────────────────

/// <summary>
/// JSON-judge: does the agent correctly reference information from earlier turns?
/// Returns structured output capturing referenced facts and memory errors.
/// Default policy: TrackTrend.
/// </summary>
public sealed class MemoryAccuracyEvaluator : HpdJsonJudgeEvaluatorBase<MemoryAccuracyRating>
{
    public const string MetricName = "Memory Accuracy";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var historyCtx = additionalContext?.OfType<ConversationHistoryContext>().FirstOrDefault();
        var history = historyCtx?.History ?? [];
        var historyText = string.Join("\n", history.Take(15)
            .Select(m => $"{m.Role.Value}: {m.Text?[..Math.Min(300, m.Text?.Length ?? 0)]}"));

        return
        [
            new(ChatRole.System,
                "Evaluate whether the agent correctly remembers and references information from prior turns. " +
                "Return JSON: {\"score\": 0.0-1.0, \"facts_referenced\": [\"...\"], \"memory_errors\": [\"...\"]}. " +
                "score=1 = all referenced facts are accurate; score=0 = significant memory errors."),
            new(ChatRole.User,
                $"Prior conversation:\n{historyText}\n\nCurrent response:\n{modelResponse.Text}"),
        ];
    }

    protected override MemoryAccuracyRating? ParseRating(string json)
    {
        try { return JsonSerializer.Deserialize<MemoryAccuracyRating>(json); }
        catch { return null; }
    }

    protected override void PopulateResult(
        MemoryAccuracyRating rating, EvaluationResult result,
        ChatResponse judgeResponse, TimeSpan duration)
    {
        var metric = (NumericMetric)result.Metrics.Values.First();
        metric.Value = Math.Clamp(rating.Score, 0.0, 1.0);
        if (rating.MemoryErrors?.Length > 0)
        {
            metric.Reason = $"Memory errors: {string.Join("; ", rating.MemoryErrors)}";
            metric.AddOrUpdateMetadata("memory_errors", string.Join("; ", rating.MemoryErrors));
        }
        if (rating.FactsReferenced?.Length > 0)
            metric.AddOrUpdateMetadata("facts_referenced", string.Join("; ", rating.FactsReferenced));
        metric.AddOrUpdateChatMetadata(judgeResponse, duration);
    }
}

public sealed class MemoryAccuracyRating
{
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("facts_referenced")] public string[]? FactsReferenced { get; set; }
    [JsonPropertyName("memory_errors")] public string[]? MemoryErrors { get; set; }
}
