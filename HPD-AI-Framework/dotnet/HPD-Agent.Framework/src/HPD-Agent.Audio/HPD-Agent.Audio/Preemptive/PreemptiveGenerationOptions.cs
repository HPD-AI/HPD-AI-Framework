// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Preemptive;

/// <summary>
/// Options for speculative generation candidate selection.
/// </summary>
public sealed record PreemptiveGenerationOptions
{
    /// <summary>Minimum transcript confidence required to start speculative work.</summary>
    public float ConfidenceThreshold { get; init; } = 0.7f;
}
