// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// User turn controller state.
/// </summary>
public enum TurnControllerState
{
    /// <summary>No active user turn.</summary>
    Idle,

    /// <summary>User speech is currently active.</summary>
    UserSpeaking,

    /// <summary>Speech appears to have ended, but endpointing has not committed.</summary>
    UserMaybeDone,

    /// <summary>Speech ended before a usable transcript arrived.</summary>
    AwaitingTranscript,

    /// <summary>A transcript is ready and endpointing delay is pending.</summary>
    AwaitingEndpointDelay,

    /// <summary>The turn has been committed as agent input.</summary>
    Committed,

    /// <summary>The turn was cancelled.</summary>
    Cancelled
}
