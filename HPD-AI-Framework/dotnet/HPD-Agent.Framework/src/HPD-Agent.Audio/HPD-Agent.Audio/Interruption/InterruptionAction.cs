// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Interruption;

/// <summary>
/// Action requested by the interruption controller.
/// </summary>
public enum InterruptionAction
{
    /// <summary>No output action is required.</summary>
    None,

    /// <summary>Pause active output while waiting for transcript evidence.</summary>
    PauseOutput,

    /// <summary>Resume paused output after false-interruption recovery.</summary>
    ResumeOutput,

    /// <summary>Interrupt active output and cancel stale generation.</summary>
    InterruptOutput
}
