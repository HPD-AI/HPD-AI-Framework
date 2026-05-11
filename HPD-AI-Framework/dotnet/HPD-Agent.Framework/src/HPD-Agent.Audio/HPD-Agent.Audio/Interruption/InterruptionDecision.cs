// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.Interruption;

/// <summary>
/// Decision returned by the interruption controller.
/// </summary>
public sealed record InterruptionDecision
{
    /// <summary>Current interruption state after processing input.</summary>
    public InterruptionDecisionState State { get; init; }

    /// <summary>Requested output action.</summary>
    public InterruptionAction Action { get; init; }

    /// <summary>Stable reason for metrics, tests, and replay.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Recognized transcript involved in the decision, if any.</summary>
    public string? TranscriptText { get; init; }

    /// <summary>Output context involved in the decision, if any.</summary>
    public SpeechOutputContext? OutputContext { get; init; }
}
