// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations;

/// <summary>
/// Primary evaluation data object built by EvaluationMiddleware from AfterMessageTurnContext
/// and handed to every evaluator. Rich, typed, zero-reconstruction — all data comes directly
/// from HPD's in-memory typed objects (ChatMessage, FunctionCallContent, etc.).
/// </summary>
public sealed class TurnEvaluationContext
{
    // ── Identity ─────────────────────────────────────────────────────────────

    public string AgentName { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>
    /// Zero-based index of this turn within the branch.
    /// Computed as the count of user-role ChatMessages in Branch.Messages that precede
    /// the current turn's input message. The first turn in a branch is TurnIndex = 0.
    /// Used as part of the deduplication key in IScoreStore.
    /// </summary>
    public int TurnIndex { get; init; }

    // ── Input ─────────────────────────────────────────────────────────────────

    public string UserInput { get; init; } = string.Empty;

    /// <summary>Prior turns — does not include the current turn's messages.</summary>
    public IReadOnlyList<ChatMessage> ConversationHistory { get; init; } = [];

    // ── Output ────────────────────────────────────────────────────────────────

    public string OutputText { get; init; } = string.Empty;
    public ChatResponse FinalResponse { get; init; } = null!;

    /// <summary>Aggregated reasoning/thinking tokens for this turn. Unique to HPD.</summary>
    public string? ReasoningText { get; init; }

    // ── Tool execution ────────────────────────────────────────────────────────

    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];
    public TurnTrace Trace { get; init; } = null!;

    // ── Performance ───────────────────────────────────────────────────────────

    public UsageDetails? TurnUsage { get; init; }
    public IReadOnlyList<UsageDetails?> IterationUsage { get; init; } = [];
    public int IterationCount { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ModelId { get; init; }
    public string? ProviderKey { get; init; }

    // ── Mid-run instrumentation (set via EvalContext.SetAttribute / IncrementMetric) ──

    public IReadOnlyDictionary<string, object> Attributes { get; init; } =
        new Dictionary<string, object>();

    public IReadOnlyDictionary<string, double> Metrics { get; init; } =
        new Dictionary<string, double>();

    // ── Agent stop classification ──────────────────────────────────────────────

    /// <summary>
    /// Why Claude stopped at the end of this turn. Computed by TurnEvaluationContextBuilder
    /// from the final output text and the last iteration's FinishReason.
    /// Used by TurnAutonomyEvaluator and available for IScoreStore analytics.
    /// </summary>
    public AgentStopKind StopKind { get; init; }

    // ── CI / ground truth ─────────────────────────────────────────────────────

    public string? GroundTruth { get; init; }

    /// <summary>From RunConfig.ContextOverrides — experiment-level key/value pairs.</summary>
    public IDictionary<string, object>? ExperimentContext { get; init; }
}

/// <summary>
/// Classifies why an agent turn ended. Distinguishes agent-initiated stops (clarification,
/// credential requests, confirmation gates) from task completion. Matches the categories
/// identified in Anthropic's "Measuring AI Agent Autonomy in Practice" (2026) study.
/// </summary>
public enum AgentStopKind
{
    /// <summary>Agent finished the task without asking for human input.</summary>
    Completed,

    /// <summary>
    /// Agent ended the turn with a question to the user (output ends in '?' or
    /// the last iteration finish reason signals a clarification pause).
    /// Correlates with lower autonomy scores.
    /// </summary>
    AskedClarification,

    /// <summary>Agent stopped because it needs credentials, tokens, or access it doesn't have.</summary>
    RequestedCredentials,

    /// <summary>Agent explicitly asked for user confirmation before proceeding.</summary>
    AwaitingConfirmation,

    /// <summary>Stop kind could not be determined from available context.</summary>
    Unknown,
}

/// <summary>
/// A read-only, evaluator-friendly projection of a single tool call and its result.
/// Constructed by TurnEvaluationContextBuilder from:
/// - FunctionCallContent + FunctionResultContent pairs in TurnHistory
/// - ToolCallStartEvent / ToolCallEndEvent timestamps buffered by EvaluationMiddleware
/// - PermissionDeniedEvent.CallId records buffered by EvaluationMiddleware
/// </summary>
public sealed record ToolCallRecord(
    string CallId,
    string Name,
    string? ToolkitName,
    string ArgumentsJson,
    string Result,
    TimeSpan Duration,
    bool WasPermissionDenied);
