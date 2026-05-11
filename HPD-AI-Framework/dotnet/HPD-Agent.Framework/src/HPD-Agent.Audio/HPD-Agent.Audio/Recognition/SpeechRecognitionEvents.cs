// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Events;

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Base type for normalized HPD speech recognition events.
/// </summary>
public abstract record SpeechRecognitionEvent : AgentEvent
{
    /// <summary>Shared correlation and timing context.</summary>
    public required SpeechRecognitionContext Context { get; init; }
}

/// <summary>Emitted when speech recognition observes the start of an utterance.</summary>
public sealed record SpeechRecognitionStartedEvent : SpeechRecognitionEvent
{
    /// <summary>Optional speech probability reported by VAD or provider.</summary>
    public float? SpeechProbability { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted for volatile transcript text that may be revised.</summary>
public sealed record SpeechRecognitionInterimEvent : SpeechRecognitionEvent
{
    /// <summary>The interim transcript payload.</summary>
    public required SpeechRecognitionTranscript Transcript { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted for stable transcript text suitable for speculative work.</summary>
public sealed record SpeechRecognitionPreflightEvent : SpeechRecognitionEvent
{
    /// <summary>The preflight transcript payload.</summary>
    public required SpeechRecognitionTranscript Transcript { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted for a provider or adapter committed transcript segment.</summary>
public sealed record SpeechRecognitionFinalEvent : SpeechRecognitionEvent
{
    /// <summary>The final transcript payload.</summary>
    public required SpeechRecognitionTranscript Transcript { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>Emitted for recognition usage, duration, or provider metric updates.</summary>
public sealed record SpeechRecognitionUsageEvent : SpeechRecognitionEvent
{
    /// <summary>Recognized audio duration, if known.</summary>
    public TimeSpan? AudioDuration { get; init; }

    /// <summary>Number of input bytes processed, if known.</summary>
    public long? InputBytes { get; init; }

    /// <summary>Provider-specific numeric metrics.</summary>
    public IReadOnlyDictionary<string, double>? ProviderMetrics { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Diagnostic;
}

/// <summary>Emitted when recognizer-observed speech ends.</summary>
public sealed record SpeechRecognitionEndedEvent : SpeechRecognitionEvent
{
    /// <summary>Observed speech duration, if known.</summary>
    public TimeSpan? SpeechDuration { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>Emitted when recognition fails.</summary>
public sealed record SpeechRecognitionErrorEvent : SpeechRecognitionEvent
{
    /// <summary>Human-readable error text.</summary>
    public required string Error { get; init; }

    /// <summary>Provider or adapter error code, if available.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Whether the recognition session should be treated as failed.</summary>
    public bool IsFatal { get; init; } = true;

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Diagnostic;
}
