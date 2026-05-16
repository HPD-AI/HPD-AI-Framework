// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// HPD-native record for one evaluated case/turn/run.
/// This preserves the full reporting payload that MS ScenarioRunResult carries,
/// plus HPD provenance that matters for agents, branches, datasets, and policy.
/// </summary>
public sealed class EvaluationRunRecord
{
    public string Id { get; init; } = string.Empty;

    // ── Run grouping ─────────────────────────────────────────────────────────

    public string ExecutionName { get; init; } = string.Empty;
    public string ScenarioName { get; init; } = string.Empty;
    public string IterationName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }

    // ── Full evaluation payload ──────────────────────────────────────────────

    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];
    public ChatResponse ModelResponse { get; init; } = null!;
    public EvaluationResult EvaluationResult { get; init; } = new();

    /// <summary>
    /// Optional MS chat-details payload for compatibility exports. HPD does not
    /// require callers to produce it, but keeps it when imported from MS reporting.
    /// </summary>
    public ChatDetails? ChatDetails { get; init; }

    /// <summary>
    /// HPD-native judge-call traces for the evaluators run in this case/turn.
    /// This is the primary HPD reporting surface; ChatDetails is compatibility-only.
    /// </summary>
    public IReadOnlyList<JudgeCallRecord> JudgeCalls { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    // ── HPD provenance ───────────────────────────────────────────────────────

    public EvaluationSource Source { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string? ProviderKey { get; init; }
    public string? ModelId { get; init; }
    public string? ResponseModelId { get; init; }

    // ── Dataset provenance ──────────────────────────────────────────────────

    public string? DatasetId { get; init; }
    public string? DatasetVersion { get; init; }
    public string? CaseId { get; init; }
    public string? CaseVersion { get; init; }
    public DateTimeOffset? CaseValidFrom { get; init; }
    public DateTimeOffset? CaseValidTo { get; init; }

    // ── Durations ────────────────────────────────────────────────────────────

    public TimeSpan TaskDuration { get; init; }
    public TimeSpan EvaluatorDuration { get; init; }
    public TimeSpan TotalDuration { get; init; }

    public ScenarioRunResult ToScenarioRunResult() =>
        new(
            ScenarioName,
            IterationName,
            ExecutionName,
            CreatedAt.UtcDateTime,
            Messages,
            ModelResponse,
            EvaluationResult,
            ChatDetails,
            Tags);

    public static EvaluationRunRecord FromScenarioRunResult(ScenarioRunResult result) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ExecutionName = result.ExecutionName,
            ScenarioName = result.ScenarioName,
            IterationName = result.IterationName,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(result.CreationTime, DateTimeKind.Utc)),
            Messages = [.. result.Messages],
            ModelResponse = result.ModelResponse,
            EvaluationResult = result.EvaluationResult,
            ChatDetails = result.ChatDetails,
            Tags = result.Tags is null ? [] : [.. result.Tags],
            Source = EvaluationSource.Test,
            AgentName = result.ExecutionName,
            SessionId = result.ExecutionName,
            BranchId = result.ScenarioName,
            TurnIndex = 0,
        };
}
