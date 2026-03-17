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
    public string BranchId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string? ModelId { get; init; }

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

    // ── Sampling ──────────────────────────────────────────────────────────────

    public double SamplingRate { get; init; }
    public EvalPolicy Policy { get; init; }

    // ── Timestamps ────────────────────────────────────────────────────────────

    public DateTimeOffset CreatedAt { get; init; }
}
