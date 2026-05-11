// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Interruption;

/// <summary>
/// State of the interruption decision process.
/// </summary>
public enum InterruptionDecisionState
{
    /// <summary>No active agent speech is playing.</summary>
    NoActiveSpeech,

    /// <summary>User speech may be a short backchannel.</summary>
    PotentialBackchannel,

    /// <summary>User speech may interrupt active agent speech.</summary>
    PotentialInterruption,

    /// <summary>User speech is confirmed as an interruption.</summary>
    ConfirmedInterruption,

    /// <summary>User speech was treated as a false interruption.</summary>
    FalseInterruption,

    /// <summary>Paused output recovered from a false interruption.</summary>
    Recovered
}
