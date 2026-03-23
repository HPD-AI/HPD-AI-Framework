// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators;

/// <summary>
/// HPD's extension of the MS IEvaluator interface. Adds versioning so that evaluator
/// prompt changes can be tracked independently in IScoreStore without invalidating
/// existing score history.
/// </summary>
public interface IHpdEvaluator : IEvaluator
{
    /// <summary>
    /// Semantic version of this evaluator. Changing the judge prompt, scoring rubric,
    /// or output schema increments the version so old and new scores remain separately
    /// queryable. Defaults to "1.0.0" in the base class.
    /// </summary>
    string Version { get; }
}

/// <summary>
/// Base class for all HPD evaluators. Provides default versioning.
/// Implementors override Version when their scoring logic changes.
/// </summary>
public abstract class HpdEvaluatorBase : IHpdEvaluator
{
    public virtual string Version => "1.0.0";

    public abstract IReadOnlyCollection<string> EvaluationMetricNames { get; }

    public abstract ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for deterministic evaluators that never make LLM calls.
/// Seals away the chatConfiguration parameter — deterministic subclasses never see it.
/// All Section 4.2 evaluators extend this class.
/// </summary>
public abstract class HpdDeterministicEvaluatorBase : HpdEvaluatorBase
{
    public sealed override ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
        => EvaluateDeterministicAsync(messages, modelResponse, additionalContext, cancellationToken);

    protected abstract ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken);
}

/// <summary>
/// Internal extension methods for HPD evaluator metric metadata.
/// Replicates the internal MS BuiltInMetricUtilities.MarkAsBuiltIn() behavior so HPD
/// evaluators appear in the MS HTML report viewer as built-in evaluators.
/// </summary>
internal static class HpdEvalMetricExtensions
{
    internal static void MarkAsHpdBuiltIn(this EvaluationMetric metric)
        => metric.AddOrUpdateMetadata("built-in-eval", "True");

    internal static void MarkAllMetricsAsHpdBuiltIn(this EvaluationResult result)
        => result.AddOrUpdateMetadataInAllMetrics("built-in-eval", "True");
}
