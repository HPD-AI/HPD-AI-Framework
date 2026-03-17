// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;

namespace HPD.Agent.Evaluations.Evaluators.LlmJudge;

/// <summary>
/// Shared XML-tag parsing helper for HPD single-call LLM judge evaluators.
/// Parses the standard S0/S1/S2 response format used by MS quality evaluators.
/// </summary>
internal static class TagParser
{
    private static readonly Regex _tagRegex = new(
        @"<S(?<tag>\d+)>(?<value>.*?)</S\k<tag>>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Extracts S0 (thinking), S1 (reason), S2 (score) from the judge response.
    /// Returns null for missing tags.
    /// </summary>
    internal static (string? thinking, string? reason, string? score) Parse(string text)
    {
        string? thinking = null, reason = null, score = null;
        foreach (Match m in _tagRegex.Matches(text))
        {
            switch (m.Groups["tag"].Value)
            {
                case "0": thinking = m.Groups["value"].Value.Trim(); break;
                case "1": reason = m.Groups["value"].Value.Trim(); break;
                case "2": score = m.Groups["value"].Value.Trim(); break;
            }
        }
        return (thinking, reason, score);
    }

    internal static bool TryParseDouble(string? raw, out double value)
        => double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    internal static bool TryParseBool(string? raw, out bool value)
    {
        value = false;
        if (raw is null) return false;
        var lower = raw.ToLowerInvariant().Trim();
        if (lower is "true" or "yes" or "1" or "pass") { value = true; return true; }
        if (lower is "false" or "no" or "0" or "fail") { value = false; return true; }
        return false;
    }
}

// ── ContextRelevanceEvaluator ────────────────────────────────────────────────

/// <summary>
/// Scores how relevant the retrieved context is to the user's query (0–1).
/// Requires GroundingDocumentContext. Default policy: TrackTrend.
/// </summary>
public sealed class ContextRelevanceEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Context Relevance";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var groundingCtx = additionalContext?.OfType<GroundingDocumentContext>().FirstOrDefault();
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;
        var chunks = groundingCtx?.Resolve(turnCtx) ?? [];
        var query = turnCtx?.UserInput ?? modelResponse.Text ?? string.Empty;

        return
        [
            new(ChatRole.System,
                "Rate how relevant the retrieved context is to the user query on a scale of 0 to 1, " +
                "where 0 = completely irrelevant and 1 = perfectly relevant. " +
                "Think step by step in <S0> tags, explain in <S1> tags, give the score in <S2> tags."),
            new(ChatRole.User,
                $"Query: {query}\n\nContext:\n{string.Join("\n---\n", chunks)}"),
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

// ── AnswerSimilarityEvaluator ────────────────────────────────────────────────

/// <summary>
/// Semantic similarity between the agent's answer and the ground truth (0–1).
/// Requires GroundTruthContext. Default policy: TrackTrend.
/// </summary>
public sealed class AnswerSimilarityEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Answer Similarity";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var gtCtx = additionalContext?.OfType<GroundTruthContext>().FirstOrDefault();
        return
        [
            new(ChatRole.System,
                "Rate the semantic similarity between the predicted answer and the reference answer " +
                "on a scale of 0 to 1, where 0 = completely different and 1 = semantically identical. " +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Reference: {gtCtx?.Expected ?? "(none)"}\n\nPredicted: {modelResponse.Text}"),
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

// ── AspectCriticEvaluator ────────────────────────────────────────────────────

/// <summary>
/// Boolean evaluator that grades the response against a custom rubric.
/// Returns true (pass) or false (fail) based on whether the response meets the criterion.
/// Default policy: MustAlwaysPass.
/// </summary>
public sealed class AspectCriticEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Aspect Critic";
    private readonly string _rubric;

    public AspectCriticEvaluator(string rubric) => _rubric = rubric;

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new BooleanMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        return
        [
            new(ChatRole.System,
                $"Evaluate whether the response satisfies the following criterion: {_rubric}\n" +
                "Think step by step in <S0>, explain in <S1>, answer true or false in <S2>."),
            new(ChatRole.User, $"Response: {modelResponse.Text}"),
        ];
    }

    protected override void ParseJudgeResponse(
        string responseText, EvaluationResult result, ChatResponse judgeResponse, TimeSpan duration)
    {
        var (_, reason, score) = TagParser.Parse(responseText);
        var metric = (BooleanMetric)result.Metrics.Values.First();
        if (TagParser.TryParseBool(score, out var v))
            metric.Value = v;
        metric.Reason = reason;
        metric.AddOrUpdateChatMetadata(judgeResponse, duration);
    }
}

// ── GoalAccuracyEvaluator ────────────────────────────────────────────────────

/// <summary>
/// Scores how accurately the agent achieved the described goal (0–1).
/// Requires desired outcome and achieved outcome via metadata or context.
/// Default policy: TrackTrend.
/// </summary>
public sealed class GoalAccuracyEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Goal Accuracy";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;
        var groundTruth = additionalContext?.OfType<GroundTruthContext>().FirstOrDefault()?.Expected
            ?? turnCtx?.GroundTruth;

        return
        [
            new(ChatRole.System,
                "Rate how accurately the agent achieved the described goal on a scale of 0 to 1. " +
                "0 = completely missed, 1 = fully achieved. Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Goal: {groundTruth ?? "(not specified)"}\n\nAgent response: {modelResponse.Text}"),
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

// ── TopicAdherenceEvaluator ──────────────────────────────────────────────────

/// <summary>
/// Measures what fraction of the allowed topics the response stays within (0–1).
/// Default policy: TrackTrend.
/// </summary>
public sealed class TopicAdherenceEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Topic Adherence";
    private readonly IReadOnlyList<string> _allowedTopics;

    public TopicAdherenceEvaluator(IReadOnlyList<string> allowedTopics)
        => _allowedTopics = allowedTopics;

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var topics = string.Join(", ", _allowedTopics);
        return
        [
            new(ChatRole.System,
                $"Rate how well the response stays on topic. Allowed topics: {topics}. " +
                "Score 0–1 where 1 = completely on topic. Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User, $"Response: {modelResponse.Text}"),
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

// ── CustomJudgeEvaluator ─────────────────────────────────────────────────────

/// <summary>
/// Flexible evaluator that scores the response against a caller-supplied rubric (0–1).
/// Also emits a BooleanMetric pass/fail based on score >= 0.5.
/// Default policy: TrackTrend.
/// </summary>
public sealed class CustomJudgeEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Custom Judge";
    private readonly string _rubric;

    public CustomJudgeEvaluator(string rubric) => _rubric = rubric;

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        return
        [
            new(ChatRole.System,
                $"Evaluate the response on a scale of 0 to 1 based on this criterion: {_rubric}\n" +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User, $"Response: {modelResponse.Text}"),
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

// ── TaskSuccessEvaluator ─────────────────────────────────────────────────────

/// <summary>
/// Boolean evaluator: did the agent successfully complete the task it was given?
/// Based on UserInput and OutputText. Default policy: TrackTrend.
/// </summary>
public sealed class TaskSuccessEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Task Success";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new BooleanMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;
        var userInput = turnCtx?.UserInput
            ?? messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text
            ?? string.Empty;

        return
        [
            new(ChatRole.System,
                "Did the agent successfully complete the task? Answer true if the task appears fully " +
                "completed, false if the response is incomplete, refuses the task, or only partially " +
                "addresses it. Think in <S0>, explain in <S1>, answer true/false in <S2>."),
            new(ChatRole.User,
                $"Task: {userInput}\n\nAgent response: {modelResponse.Text}"),
        ];
    }

    protected override void ParseJudgeResponse(
        string responseText, EvaluationResult result, ChatResponse judgeResponse, TimeSpan duration)
    {
        var (_, reason, score) = TagParser.Parse(responseText);
        var metric = (BooleanMetric)result.Metrics.Values.First();
        if (TagParser.TryParseBool(score, out var v))
            metric.Value = v;
        metric.Reason = reason;
        metric.AddOrUpdateChatMetadata(judgeResponse, duration);
    }
}
