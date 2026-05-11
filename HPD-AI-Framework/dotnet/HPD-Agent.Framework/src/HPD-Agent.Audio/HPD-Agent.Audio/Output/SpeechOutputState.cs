// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Output;

/// <summary>
/// Snapshot of generated, queued, and played speech output state.
/// </summary>
public sealed record SpeechOutputState
{
    /// <summary>Total generated audio duration when known.</summary>
    public TimeSpan GeneratedDuration { get; init; }

    /// <summary>Total queued audio duration when known.</summary>
    public TimeSpan QueuedDuration { get; init; }

    /// <summary>Total played audio duration when known.</summary>
    public TimeSpan PlayedDuration { get; init; }

    /// <summary>Total generated or queued audio duration discarded before playback.</summary>
    public TimeSpan DiscardedDuration { get; init; }

    /// <summary>Total generated audio duration currently held during false-interruption recovery.</summary>
    public TimeSpan HeldDuration { get; init; }

    /// <summary>Current playback position when known.</summary>
    public TimeSpan PlaybackPosition { get; init; }

    /// <summary>Number of audio chunks queued for output.</summary>
    public int QueuedChunks { get; init; }

    /// <summary>Number of audio chunks emitted to downstream consumers.</summary>
    public int EmittedChunks { get; init; }

    /// <summary>Number of audio chunks reported as played.</summary>
    public int PlayedChunks { get; init; }

    /// <summary>Number of generated audio chunks currently held during false-interruption recovery.</summary>
    public int HeldChunks { get; init; }

    /// <summary>Whether output is currently paused and holding unplayed audio.</summary>
    public bool IsPaused { get; init; }

    /// <summary>Whether this output has been interrupted.</summary>
    public bool Interrupted { get; init; }

    /// <summary>Text synchronized with this output when known.</summary>
    public string? SynchronizedTranscript { get; init; }
}
