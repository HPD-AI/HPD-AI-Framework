// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;

namespace HPD.Agent.Evaluations.Evaluators.LlmJudge;

// ── ReasoningCoherenceEvaluator ──────────────────────────────────────────────

/// <summary>
/// LLM-as-judge: does the chain-of-thought reasoning logically lead to the output? (0–1)
/// Unique to HPD — no other framework exposes raw reasoning tokens to evaluators.
/// Requires ReasoningContext (auto-populated by EvaluationMiddleware from TurnEvaluationContext).
/// Default policy: TrackTrend.
/// </summary>
public sealed class ReasoningCoherenceEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Reasoning Coherence";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var reasoningCtx = additionalContext?.OfType<ReasoningContext>().FirstOrDefault();
        var reasoning = reasoningCtx?.Reasoning
            ?? additionalContext?.OfType<TurnEvaluationContextWrapper>()
                .FirstOrDefault()?.Context.ReasoningText
            ?? "(no reasoning available)";

        return
        [
            new(ChatRole.System,
                "Rate how coherently the reasoning leads to the final response on a scale of 0 to 1. " +
                "0 = reasoning is contradictory or irrelevant to the output. " +
                "1 = reasoning clearly and logically supports every step to the output. " +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Reasoning:\n{reasoning}\n\nFinal response:\n{modelResponse.Text}"),
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

// ── ReasoningGroundednessEvaluator ───────────────────────────────────────────

/// <summary>
/// LLM-as-judge: does the reasoning reference actual tool results and context,
/// rather than hallucinating facts? (0–1)
/// Requires ReasoningContext. Default policy: TrackTrend.
/// </summary>
public sealed class ReasoningGroundednessEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Reasoning Groundedness";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var reasoningCtx = additionalContext?.OfType<ReasoningContext>().FirstOrDefault();
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;
        var reasoning = reasoningCtx?.Reasoning ?? turnCtx?.ReasoningText ?? "(no reasoning available)";

        var toolResults = turnCtx?.ToolCalls.Count > 0
            ? string.Join("\n", turnCtx.ToolCalls.Select(tc =>
                $"- {tc.Name}: {tc.Result[..Math.Min(300, tc.Result.Length)]}"))
            : "(no tool calls)";

        return
        [
            new(ChatRole.System,
                "Rate how well the reasoning is grounded in actual tool results and provided context, " +
                "rather than assuming or inventing facts. Score 0–1: 0 = fully hallucinated reasoning, " +
                "1 = all claims in reasoning traceable to tool outputs or provided context. " +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Tool results:\n{toolResults}\n\n" +
                $"Reasoning:\n{reasoning}"),
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

// ── ReasoningEfficiencyEvaluator ─────────────────────────────────────────────

/// <summary>
/// LLM-as-judge: penalizes excessive or verbose reasoning relative to the task complexity.
/// Score 0–1 where 1 = perfectly efficient reasoning, 0 = massively over-reasoned.
/// Default policy: TrackTrend.
/// </summary>
public sealed class ReasoningEfficiencyEvaluator : HpdLlmJudgeEvaluatorBase
{
    public const string MetricName = "Reasoning Efficiency";
    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var reasoningCtx = additionalContext?.OfType<ReasoningContext>().FirstOrDefault();
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;
        var reasoning = reasoningCtx?.Reasoning ?? turnCtx?.ReasoningText ?? "(no reasoning available)";
        var reasoningLen = reasoning.Length;
        var outputLen = modelResponse.Text?.Length ?? 0;

        return
        [
            new(ChatRole.System,
                "Rate the efficiency of the reasoning relative to task complexity. " +
                "Score 0–1: 1 = minimal, targeted reasoning for the task difficulty; " +
                "0 = vastly over-reasoned for a simple task. " +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Task: {turnCtx?.UserInput ?? "(unknown)"}\n" +
                $"Reasoning length: ~{reasoningLen} chars\n" +
                $"Output length: ~{outputLen} chars\n\n" +
                $"Reasoning (truncated):\n{reasoning[..Math.Min(800, reasoningLen)]}"),
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
