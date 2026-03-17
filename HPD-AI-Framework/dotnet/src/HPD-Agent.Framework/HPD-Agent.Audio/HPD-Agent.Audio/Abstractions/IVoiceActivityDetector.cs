// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio;

/// <summary>
/// Detects voice activity in audio streams.
/// Enables fast interruption detection (~10ms latency) before STT completes.
/// </summary>
public interface IVoiceActivityDetector : IDisposable
{
    /// <summary>
    /// Processes audio frames and yields VAD events.
    /// Encapsulates state machine logic, pre-speech buffering, and event emission.
    /// </summary>
    /// <param name="audio">Async enumerable of audio frames to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of VAD events.</returns>
    IAsyncEnumerable<VadEvent> DetectAsync(
        IAsyncEnumerable<AudioFrame> audio,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a single audio frame synchronously.
    /// </summary>
    /// <param name="frame">The audio frame to process.</param>
    /// <returns>The VAD result for this frame.</returns>
    VadResult Process(AudioFrame frame);

    /// <summary>
    /// Resets internal state for a new audio session.
    /// </summary>
    void Reset();
}

/// <summary>
/// VAD event types.
/// </summary>
public enum VadEventType
{
    /// <summary>User started speaking.</summary>
    StartOfSpeech,

    /// <summary>VAD inference completed (includes probability).</summary>
    InferenceDone,

    /// <summary>User stopped speaking.</summary>
    EndOfSpeech
}

/// <summary>
/// Event emitted by the voice activity detector.
/// </summary>
public record VadEvent
{
    /// <summary>
    /// The type of VAD event.
    /// </summary>
    public required VadEventType Type { get; init; }

    /// <summary>
    /// Timestamp of this event relative to stream start.
    /// </summary>
    public required TimeSpan Timestamp { get; init; }

    /// <summary>
    /// Probability that this frame contains speech (0.0 to 1.0).
    /// </summary>
    public float SpeechProbability { get; init; }

    /// <summary>
    /// Duration of continuous speech so far.
    /// </summary>
    public TimeSpan SpeechDuration { get; init; }

    /// <summary>
    /// Duration of continuous silence so far.
    /// </summary>
    public TimeSpan SilenceDuration { get; init; }

    /// <summary>
    /// Audio frames associated with this event.
    /// For EndOfSpeech, contains the complete user speech including pre-speech buffer.
    /// </summary>
    public IReadOnlyList<AudioFrame>? Frames { get; init; }
}

/// <summary>
/// Represents a single audio frame for processing.
/// </summary>
public readonly struct AudioFrame
{
    /// <summary>
    /// The raw audio data (typically 16-bit PCM).
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>
    /// Sample rate in Hz (e.g., 16000, 24000).
    /// </summary>
    public int SampleRate { get; init; }

    /// <summary>
    /// Number of audio channels (1 = mono, 2 = stereo).
    /// </summary>
    public int Channels { get; init; }

    /// <summary>
    /// Timestamp of this frame relative to stream start.
    /// </summary>
    public TimeSpan Timestamp { get; init; }

    /// <summary>
    /// Duration of audio in this frame.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Result from processing a single audio frame.
/// </summary>
public readonly struct VadResult
{
    /// <summary>
    /// Current state of the VAD state machine.
    /// </summary>
    public VadState State { get; init; }

    /// <summary>
    /// Probability that this frame contains speech (0.0 to 1.0).
    /// </summary>
    public float SpeechProbability { get; init; }

    /// <summary>
    /// Whether speech is currently detected (after debouncing).
    /// </summary>
    public bool IsSpeaking { get; init; }
}

/// <summary>
/// VAD state machine with hysteresis (prevents jitter).
/// </summary>
public enum VadState
{
    /// <summary>No voice detected.</summary>
    Quiet,

    /// <summary>Voice starting (debounce).</summary>
    Starting,

    /// <summary>Confirmed speaking.</summary>
    Speaking,

    /// <summary>Voice stopping (debounce).</summary>
    Stopping
}
