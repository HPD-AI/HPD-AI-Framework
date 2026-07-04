// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Integration;

namespace HPD.Agent.Evaluations.Annotation;

/// <summary>
/// Convenience helpers for submitting human annotation responses through the
/// agent's existing request-session event coordinator.
/// </summary>
public static class AgentAnnotationExtensions
{
    public static Task SendAnnotationResponseAsync(
        this global::HPD.Agent.Agent agent,
        string annotationId,
        string reviewerId,
        string label,
        double? score = null,
        string? comment = null,
        string? evaluatorName = null,
        string? metricName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return agent.SendAnnotationResponseAsync(new AnnotationResponseEvent
        {
            AnnotationId = annotationId,
            ReviewerId = reviewerId,
            Label = label,
            Score = score,
            Comment = comment,
            EvaluatorName = evaluatorName,
            MetricName = metricName,
        }, cancellationToken);
    }

    public static Task SendAnnotationResponseAsync(
        this global::HPD.Agent.Agent agent,
        AnnotationResponseEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(response);

        return agent.AnswerRequestAsync(response, cancellationToken);
    }
}
