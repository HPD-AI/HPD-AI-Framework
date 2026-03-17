// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Evaluators.Deterministic;

/// <summary>
/// BooleanMetric — asserts that the TurnTrace contains at least one span matching the query.
/// Used for behavioral assertions: "SearchTool was called", "any tool took > 5 seconds", etc.
/// </summary>
public sealed class HasMatchingSpanEvaluator(SpanQuery query) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Has Matching Span"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Has Matching Span");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        bool matched = query.MatchesAny(ctx.Trace);
        metric.Value = matched;
        metric.Reason = matched
            ? "TurnTrace contains a span matching the query."
            : "TurnTrace does not contain any span matching the query.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}
