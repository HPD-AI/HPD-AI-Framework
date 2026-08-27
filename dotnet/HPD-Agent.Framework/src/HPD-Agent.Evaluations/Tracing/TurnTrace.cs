// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Tracing;

/// <summary>
/// Span tree for a single agent turn. Built in LiveEvaluationMiddleware.AfterMessageTurnAsync
/// using two sources:
/// - Typed ChatMessage objects from TurnHistory (content, tool calls, reasoning, finish reason)
/// - TurnEventBuffer populated by LiveEvaluationMiddleware through an HPD.Events
///   subscription (timestamps, permission denial data)
/// </summary>
public sealed class TurnTrace
{
    public string MessageTurnId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;

    /// <summary>From AgentTurnStartedEvent.Timestamp (buffered by TurnEventBuffer).</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>From MessageTurnFinishedEvent.Duration (buffered by TurnEventBuffer).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets the effective catalog and overlay identity pinned for the turn.</summary>
    public AgentTurnCapabilityIdentity? CapabilityIdentity { get; init; }

    /// <summary>Gets unified provider/local operation timing and outcome facts observed during the turn.</summary>
    public IReadOnlyList<AgentOperationTrace> Operations { get; init; } = [];

    public IReadOnlyList<IterationSpan> Iterations { get; init; } = [];
}

/// <summary>Projects evaluation-safe timing and authority facts for one unified operation.</summary>
public sealed record AgentOperationTrace
{
    /// <summary>Gets the HPD-authoritative operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the provider-authoritative identifier when one exists.</summary>
    public string? ProviderOperationId { get; init; }
    /// <summary>Gets the operation implementation category.</summary>
    public required AgentOperationSourceKind SourceKind { get; init; }
    /// <summary>Gets the final provider state observed by this trace.</summary>
    public required AgentOperationProviderStatus Status { get; init; }
    /// <summary>Gets elapsed time from registration to provider execution start.</summary>
    public TimeSpan? AcceptedToStartLatency { get; init; }
    /// <summary>Gets elapsed provider execution time when start and finish facts exist.</summary>
    public TimeSpan? ProviderExecutionLatency { get; init; }
    /// <summary>Gets elapsed time from registration to the latest observation.</summary>
    public TimeSpan ObservationLatency { get; init; }
    /// <summary>Gets the number of provider input rounds represented by the operation.</summary>
    public int InputRoundCount { get; init; }
    /// <summary>Gets whether the provider state is terminal.</summary>
    public bool IsTerminal { get; init; }
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
/// ToolCallEndEvent; permission denial from PermissionRequestEvent /
/// PermissionResponseEvent pairs buffered by TurnEventBuffer.
/// </summary>
public sealed class ToolCallSpan
{
    public string CallId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? ToolHarnessName { get; init; }
    public string ArgumentsJson { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;

    /// <summary>ToolCallEndEvent.Timestamp - ToolCallStartEvent.Timestamp.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True if a denied PermissionResponseEvent matched this call.</summary>
    public bool WasPermissionDenied { get; init; }
}
