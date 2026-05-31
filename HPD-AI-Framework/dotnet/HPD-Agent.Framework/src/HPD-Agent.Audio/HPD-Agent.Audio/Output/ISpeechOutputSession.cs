// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Output;

/// <summary>
/// Tracks one interruptible speech output lifecycle.
/// </summary>
public interface ISpeechOutputSession : IAsyncDisposable
{
    /// <summary>Speech output correlation id.</summary>
    string SpeechId { get; }

    /// <summary>Interruptible stream id.</summary>
    string StreamId { get; }

    /// <summary>Current output state snapshot.</summary>
    SpeechOutputState State { get; }

    /// <summary>Queues text for speech output.</summary>
    ValueTask PushTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Queues audio for speech output.</summary>
    ValueTask PushAudioAsync(AudioOutputFrame frame, CancellationToken cancellationToken = default);

    /// <summary>Marks playback as started by the output sink.</summary>
    ValueTask MarkPlaybackStartedAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports playback progress from the output sink.</summary>
    ValueTask MarkPlaybackProgressAsync(
        TimeSpan playedDuration,
        TimeSpan playbackPosition,
        CancellationToken cancellationToken = default);

    /// <summary>Marks playback as finished by the output sink.</summary>
    ValueTask MarkPlaybackFinishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Flushes queued output and completes the session when appropriate.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses speech output.</summary>
    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Resumes paused speech output.</summary>
    ValueTask ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Interrupts speech output.</summary>
    ValueTask InterruptAsync(CancellationToken cancellationToken = default);

    /// <summary>Normalized speech output events.</summary>
    IAsyncEnumerable<SpeechOutputEvent> Events { get; }
}
