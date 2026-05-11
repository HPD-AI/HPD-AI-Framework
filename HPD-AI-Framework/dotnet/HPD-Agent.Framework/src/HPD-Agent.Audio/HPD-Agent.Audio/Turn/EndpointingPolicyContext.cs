// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Input context for endpointing policy decisions.
/// </summary>
public sealed record EndpointingPolicyContext
{
    /// <summary>Best transcript available for the current user turn.</summary>
    public required SpeechRecognitionTranscript Transcript { get; init; }

    /// <summary>Current turn controller state when endpointing is requested.</summary>
    public required TurnControllerState State { get; init; }

    /// <summary>Fallback reason supplied by the controller transition.</summary>
    public required string FallbackReason { get; init; }

    /// <summary>Whether user speech is currently active.</summary>
    public bool IsSpeaking { get; init; }
}
