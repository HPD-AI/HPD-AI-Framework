// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;
using HPD.Events;

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Base type for HPD user turn control events.
/// </summary>
public abstract record UserTurnEvent : AgentEvent
{
    /// <summary>Shared user turn context.</summary>
    public required UserTurnContext Context { get; init; }
}

/// <summary>Emitted when turn control observes a new user turn.</summary>
public sealed record UserTurnStartedEvent : UserTurnEvent
{
    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted when recognized text updates the current user turn.</summary>
public sealed record UserTurnUpdatedEvent : UserTurnEvent
{
    /// <summary>Best current transcript for the turn.</summary>
    public required SpeechRecognitionTranscript Transcript { get; init; }

    /// <summary>Transcript stability: interim, preflight, or final.</summary>
    public required string Stability { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted when a turn has enough evidence to schedule or perform commit.</summary>
public sealed record UserTurnReadyEvent : UserTurnEvent
{
    /// <summary>Best transcript that will be committed if no new speech arrives.</summary>
    public required SpeechRecognitionTranscript Transcript { get; init; }

    /// <summary>Endpointing decision for this readiness transition.</summary>
    public required EndpointingDecision Decision { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>Emitted when turn control commits user speech as agent input.</summary>
public sealed record UserTurnCommittedEvent : UserTurnEvent
{
    /// <summary>Committed transcript.</summary>
    public required SpeechRecognitionTranscript Transcript { get; init; }

    /// <summary>Stable commit reason.</summary>
    public required string Reason { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>Emitted when the current user turn is cancelled.</summary>
public sealed record UserTurnCancelledEvent : UserTurnEvent
{
    /// <summary>Stable cancellation reason.</summary>
    public required string Reason { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;
}
