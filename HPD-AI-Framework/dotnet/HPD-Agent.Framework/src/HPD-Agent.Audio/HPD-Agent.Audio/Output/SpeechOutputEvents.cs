// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Events;

namespace HPD.Agent.Audio.Output;

/// <summary>
/// Base type for normalized HPD speech output events.
/// </summary>
public abstract record SpeechOutputEvent : AgentEvent
{
    /// <summary>Shared correlation and timing context.</summary>
    public required SpeechOutputContext Context { get; init; }
}

/// <summary>Emitted when a speech output session starts.</summary>
public sealed record SpeechOutputStartedEvent : SpeechOutputEvent
{
    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>Emitted when text is queued for speech output.</summary>
public sealed record SpeechOutputTextQueuedEvent : SpeechOutputEvent
{
    /// <summary>Text queued for synthesis or realtime output.</summary>
    public required string Text { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted when an audio frame is queued for speech output.</summary>
public sealed record SpeechOutputAudioQueuedEvent : SpeechOutputEvent
{
    /// <summary>Queued audio frame.</summary>
    public required AudioOutputFrame Frame { get; init; }

    /// <summary>Output state after the frame was queued.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted when output playback starts.</summary>
public sealed record SpeechOutputPlaybackStartedEvent : SpeechOutputEvent
{
    /// <summary>Output state at playback start.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted while playback progresses.</summary>
public sealed record SpeechOutputPlaybackProgressEvent : SpeechOutputEvent
{
    /// <summary>Output state at this progress update.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted when playback finishes.</summary>
public sealed record SpeechOutputPlaybackFinishedEvent : SpeechOutputEvent
{
    /// <summary>Output state at playback finish.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>Emitted when speech output is paused.</summary>
public sealed record SpeechOutputPausedEvent : SpeechOutputEvent
{
    /// <summary>Pause reason.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Output state at pause time.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;
}

/// <summary>Emitted when paused speech output resumes.</summary>
public sealed record SpeechOutputResumedEvent : SpeechOutputEvent
{
    /// <summary>Pause duration when known.</summary>
    public TimeSpan? PauseDuration { get; init; }

    /// <summary>Output state at resume time.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;
}

/// <summary>Emitted when speech output is interrupted.</summary>
public sealed record SpeechOutputInterruptedEvent : SpeechOutputEvent
{
    /// <summary>Interruption reason.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Output state at interruption time.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;
}

/// <summary>Emitted when the speech output session completes.</summary>
public sealed record SpeechOutputCompletedEvent : SpeechOutputEvent
{
    /// <summary>Final output state.</summary>
    public required SpeechOutputState State { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>Emitted when speech output fails.</summary>
public sealed record SpeechOutputErrorEvent : SpeechOutputEvent
{
    /// <summary>Human-readable error text.</summary>
    public required string Error { get; init; }

    /// <summary>Provider or output sink error code, if available.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Whether the output session should be treated as failed.</summary>
    public bool IsFatal { get; init; } = true;

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Diagnostic;
}
