// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>Emitted when an online evaluator completes scoring a turn.</summary>
public sealed record EvalScoreEvent : AgentEvent
{
    public string EvaluatorName { get; init; } = string.Empty;
    public string EvaluatorVersion { get; init; } = string.Empty;
    public EvaluationResult Result { get; init; } = null!;
    public EvaluationSource Source { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public TimeSpan EvaluatorDuration { get; init; }
}

/// <summary>Emitted when an online evaluator throws an exception or times out.</summary>
public sealed record EvalFailedEvent : AgentEvent
{
    public string EvaluatorName { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>Emitted when a turn is flagged for human annotation.</summary>
public sealed record AnnotationRequestedEvent : AgentEvent
{
    public string AnnotationId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string TriggerEvaluatorName { get; init; } = string.Empty;
    public double TriggerScore { get; init; }
}

/// <summary>
/// Emitted when a MustAlwaysPass evaluator returns a failing metric in online mode.
/// Distinct from EvalFailedEvent (which signals evaluator exceptions/timeouts).
/// This signals that the evaluator ran successfully but the agent behavior was wrong.
/// </summary>
public sealed record EvalPolicyViolationEvent : AgentEvent
{
    public string EvaluatorName { get; init; } = string.Empty;
    public string MetricName { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public EvaluationResult Result { get; init; } = null!;
}
