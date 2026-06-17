// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Persistent record of a single evaluator's result for one agent turn.
/// Written to IScoreStore after each evaluation completes.
/// </summary>
public sealed class ScoreRecord
{
    public string Id { get; init; } = string.Empty;

    // ── Evaluator identity ────────────────────────────────────────────────────

    public string EvaluatorName { get; init; } = string.Empty;
    public string EvaluatorVersion { get; init; } = string.Empty;

    // ── Score outputs ─────────────────────────────────────────────────────────

    /// <summary>Full MS EvaluationResult containing metrics, diagnostics, and metadata.</summary>
    public EvaluationResult Result { get; init; } = null!;

    /// <summary>Origin of this score: Live | Test | Retroactive | Human.</summary>
    public EvaluationSource Source { get; init; }

    // ── Provenance ────────────────────────────────────────────────────────────

    public string SessionId { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string? ProviderKey { get; init; }
    public string? ModelId { get; init; }
    public string? ResponseModelId { get; init; }

    // ── Dataset provenance (offline CI / benchmark governance) ───────────────

    public string? DatasetId { get; init; }
    public string? DatasetVersion { get; init; }
    public string? CaseId { get; init; }
    public string? CaseVersion { get; init; }
    public DateTimeOffset? CaseValidFrom { get; init; }
    public DateTimeOffset? CaseValidTo { get; init; }

    // ── Red-team provenance ──────────────────────────────────────────────────

    /// <summary>Identifier of the red-team plugin that generated the case, if any.</summary>
    public string? RedTeamPluginId { get; init; }

    /// <summary>Identifier of the mutation/attack strategy applied to the case, if any.</summary>
    public string? RedTeamStrategyId { get; init; }

    /// <summary>Broad attack category, such as PromptInjection, DataLeakage, or ToolAbuse.</summary>
    public string? RedTeamCategory { get; init; }

    /// <summary>Intended severity of the generated attack case.</summary>
    public string? RedTeamSeverity { get; init; }

    /// <summary>Human-readable adversarial goal for the case.</summary>
    public string? AttackGoal { get; init; }

    /// <summary>
    /// True when the adversarial attempt succeeded. False when the agent resisted it.
    /// Null means this score is not part of a red-team run or was not classified.
    /// </summary>
    public bool? AttackSucceeded { get; init; }

    // ── Performance ───────────────────────────────────────────────────────────

    public UsageDetails? TurnUsage { get; init; }
    public TimeSpan TurnDuration { get; init; }

    // ── Mid-run instrumentation (from EvalContext) ────────────────────────────

    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
    public IReadOnlyDictionary<string, double>? Metrics { get; init; }

    // ── Judge LLM details ─────────────────────────────────────────────────────

    public string? JudgeModelId { get; init; }
    public UsageDetails? JudgeUsage { get; init; }
    public TimeSpan? JudgeDuration { get; init; }

    /// <summary>
    /// Detailed judge-model calls made by this evaluator while producing Result.
    /// These are eval traces, not user-facing thread messages.
    /// </summary>
    public IReadOnlyList<JudgeCallRecord> JudgeCalls { get; init; } = [];

    // ── Sampling ──────────────────────────────────────────────────────────────

    public double SamplingRate { get; init; }
    public EvalPolicy Policy { get; init; }

    // ── Timestamps ────────────────────────────────────────────────────────────

    public DateTimeOffset CreatedAt { get; init; }
}
