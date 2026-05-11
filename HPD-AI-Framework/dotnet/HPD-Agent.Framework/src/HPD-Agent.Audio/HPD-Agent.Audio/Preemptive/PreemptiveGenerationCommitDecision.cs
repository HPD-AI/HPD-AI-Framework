// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Preemptive;

/// <summary>
/// Decision made when a committed user turn is compared with speculative work.
/// </summary>
public sealed record PreemptiveGenerationCommitDecision
{
    /// <summary>Whether the candidate can be reused for the committed turn.</summary>
    public bool ReuseCandidate { get; init; }

    /// <summary>The active candidate considered by the decision, if any.</summary>
    public PreemptiveGenerationCandidate? Candidate { get; init; }

    /// <summary>Stable reason for metrics, tests, and replay.</summary>
    public string Reason { get; init; } = "";
}
