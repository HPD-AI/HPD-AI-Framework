// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Tracing;

/// <summary>
/// Span tree for a single agent turn. Built in EvaluationMiddleware.AfterMessageTurnAsync
/// using two sources:
/// - Typed ChatMessage objects from TurnHistory (content, tool calls, reasoning, finish reason)
/// - TurnEventBuffer populated by EvaluationMiddleware as IAgentEventObserver (timestamps,
///   permission denial data)
/// </summary>
public sealed class TurnTrace
{
    public string MessageTurnId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;

    /// <summary>From AgentTurnStartedEvent.Timestamp (buffered by TurnEventBuffer).</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>From MessageTurnFinishedEvent.Duration (buffered by TurnEventBuffer).</summary>
    public TimeSpan Duration { get; init; }

    public IReadOnlyList<IterationSpan> Iterations { get; init; } = [];
}

/// <summary>
/// One LLM call within a turn. Timing sourced from AgentTurnStartedEvent /
/// AgentTurnFinishedEvent buffered by TurnEventBuffer.
/// </summary>
public sealed class IterationSpan
{
    public int IterationNumber { get; init; }
    public UsageDetails? Usage { get; init; }
    public IReadOnlyList<ToolCallSpan> ToolCalls { get; init; } = [];
    public string? AssistantText { get; init; }
    public string? ReasoningText { get; init; }
    public string? FinishReason { get; init; }

    /// <summary>
    /// AgentTurnFinishedEvent.Timestamp - AgentTurnStartedEvent.Timestamp (from TurnEventBuffer).
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// One tool call within an iteration. Timing sourced from ToolCallStartEvent /
/// ToolCallEndEvent; permission denial from PermissionDeniedEvent.CallId — all
/// buffered by TurnEventBuffer.
/// </summary>
public sealed class ToolCallSpan
{
    public string CallId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? ToolkitName { get; init; }
    public string ArgumentsJson { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;

    /// <summary>ToolCallEndEvent.Timestamp - ToolCallStartEvent.Timestamp.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True if a PermissionDeniedEvent with matching CallId was buffered.</summary>
    public bool WasPermissionDenied { get; init; }
}
