// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Evaluations;

/// <summary>
/// Controls what a failing evaluator means — whether it is a hard CI gate
/// or a soft trend signal. Maps to the ALWAYS_PASSES / USUALLY_PASSES distinction
/// used by production coding agent teams (e.g. Gemini CLI).
/// </summary>
public enum EvalPolicy
{
    /// <summary>
    /// This evaluator must pass on every run. A failure means the agent is broken.
    /// RunEvals asserts pass rate >= 1.0 for these evaluators.
    /// EvaluationMiddleware emits EvalPolicyViolationEvent on failure.
    /// Appropriate for: behavioral assertions (tool calls, permission boundaries,
    /// output constraints) that have a deterministic correct answer.
    /// </summary>
    MustAlwaysPass,

    /// <summary>
    /// This evaluator tracks quality over time. Failures are recorded in IScoreStore
    /// and surface in dashboards but never fail CI or throw in RunEvals.
    /// Appropriate for: LLM-as-judge scores, hallucination rates, coherence metrics
    /// that are probabilistic by nature.
    /// </summary>
    TrackTrend,
}
