// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators.Deterministic;

/// <summary>BooleanMetric — the named tool was invoked at least once this turn.</summary>
public sealed class ToolWasCalledEvaluator(string toolName) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Tool Was Called"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Tool Was Called");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        metric.Value = ctx.ToolCalls.Any(t => t.Name == toolName);
        metric.Reason = metric.Value == true
            ? $"Tool '{toolName}' was called."
            : $"Tool '{toolName}' was not called. Called: [{string.Join(", ", ctx.ToolCalls.Select(t => t.Name))}].";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — the named tool was called exactly N times.</summary>
public sealed class ToolCallCountEvaluator(string toolName, int expectedCount) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Tool Call Count"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Tool Call Count");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        int actual = ctx.ToolCalls.Count(t => t.Name == toolName);
        metric.Value = actual == expectedCount;
        metric.Reason = $"Tool '{toolName}' called {actual} time(s) (expected: {expectedCount}).";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — tool was called with a specific argument value.</summary>
public sealed class ToolArgumentMatchesEvaluator(string toolName, string argumentName, string expectedValue)
    : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Tool Argument Matches"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Tool Argument Matches");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        bool found = false;
        foreach (var call in ctx.ToolCalls.Where(t => t.Name == toolName))
        {
            try
            {
                using var doc = JsonDocument.Parse(call.ArgumentsJson);
                if (doc.RootElement.TryGetProperty(argumentName, out var prop) &&
                    prop.ToString() == expectedValue)
                {
                    found = true;
                    break;
                }
            }
            catch (JsonException) { /* malformed JSON — skip */ }
        }

        metric.Value = found;
        metric.Reason = found
            ? $"Tool '{toolName}' called with {argumentName}='{expectedValue}'."
            : $"Tool '{toolName}' was not called with {argumentName}='{expectedValue}'.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — agent responded without any tool calls.</summary>
public sealed class NoToolsCalledEvaluator : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["No Tools Called"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("No Tools Called");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        metric.Value = ctx.ToolCalls.Count == 0;
        metric.Reason = ctx.ToolCalls.Count == 0
            ? "No tools were called."
            : $"{ctx.ToolCalls.Count} tool call(s) were made: [{string.Join(", ", ctx.ToolCalls.Select(t => t.Name))}].";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — tools were called in the specified order (subsequence match).</summary>
public sealed class ToolCallOrderEvaluator(string[] expectedOrder) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Tool Call Order"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Tool Call Order");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        var actualNames = ctx.ToolCalls.Select(t => t.Name).ToList();

        // Subsequence check: expected order must appear in order within actual
        int idx = 0;
        foreach (var name in actualNames)
        {
            if (idx < expectedOrder.Length && name == expectedOrder[idx])
                idx++;
        }

        metric.Value = idx == expectedOrder.Length;
        metric.Reason = metric.Value == true
            ? $"Tools called in expected order: [{string.Join(" → ", expectedOrder)}]."
            : $"Expected order [{string.Join(" → ", expectedOrder)}] not found in actual [{string.Join(", ", actualNames)}].";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}
