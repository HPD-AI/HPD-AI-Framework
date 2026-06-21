#nullable enable

using HPD.Buffers;

namespace HPD.Audio.Primitives;

/// <summary>
/// Identifies the in-memory representation of decoded audio samples.
/// </summary>
public enum AudioSampleFormat
{
    /// <summary>Signed 16-bit little-endian PCM samples.</summary>
    Pcm16 = 1
}

/// <summary>
/// Declares how PCM sample bytes are interpreted.
/// </summary>
public readonly struct AudioFormat
{
    /// <summary>Gets the sample rate in samples per second per channel.</summary>
    public required int SampleRate { get; init; }

    /// <summary>Gets the number of interleaved audio channels.</summary>
    public required int ChannelCount { get; init; }

    /// <summary>Gets the sample representation used by frame data.</summary>
    public required AudioSampleFormat SampleFormat { get; init; }
}

/// <summary>
/// Describes whether decoded audio was generated from normal payload data or recovery.
/// </summary>
public enum AudioRecoveryKind
{
    /// <summary>The frame contains normally decoded or captured audio.</summary>
    None = 0,

    /// <summary>The frame was synthesized by packet-loss concealment.</summary>
    PacketLossConcealment = 1,

    /// <summary>The frame was recovered using forward error correction.</summary>
    ForwardErrorCorrection = 2,

    /// <summary>The frame was recovered from redundant encoded audio.</summary>
    RedundantEncoding = 3
}

/// <summary>
/// Describes typed frame properties that are cheap to inspect on the hot path.
/// </summary>
[Flags]
public enum AudioFrameFlags
{
    /// <summary>No flags are set.</summary>
    None = 0,

    /// <summary>The frame is the first frame after a discontinuity.</summary>
    Discontinuity = 1 << 0,

    /// <summary>The frame marks a speech or media segment boundary.</summary>
    SegmentBoundary = 1 << 1,

    /// <summary>The frame was created from a clock-adjustment or resampling boundary.</summary>
    ClockAdjusted = 1 << 2
}

/// <summary>
/// Represents a fixed-duration block of decoded PCM audio.
/// </summary>
public readonly struct AudioFrame
{
    /// <summary>Gets PCM bytes in little-endian, interleaved channel order.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>Gets the format of the PCM bytes.</summary>
    public required AudioFormat Format { get; init; }

    /// <summary>Gets the number of samples per channel in this frame.</summary>
    public required int SamplesPerChannel { get; init; }

    /// <summary>Gets an optional stream-local sequence number.</summary>
    public long? SequenceNumber { get; init; }

    /// <summary>Gets an optional capture timestamp relative to the producing stream.</summary>
    public TimeSpan? CaptureTime { get; init; }

    /// <summary>Gets an optional receive or production timestamp.</summary>
    public DateTimeOffset? ObservedAt { get; init; }

    /// <summary>Gets the recovery status for this frame.</summary>
    public AudioRecoveryKind RecoveryKind { get; init; }

    /// <summary>Gets typed hot-path flags.</summary>
    public AudioFrameFlags Flags { get; init; }

    /// <summary>Gets the duration represented by this frame.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)SamplesPerChannel / Format.SampleRate);

    /// <summary>Gets a value indicating whether this frame was produced by a recovery mechanism.</summary>
    public bool IsConcealed =>
        RecoveryKind is AudioRecoveryKind.PacketLossConcealment
            or AudioRecoveryKind.ForwardErrorCorrection
            or AudioRecoveryKind.RedundantEncoding;
}

/// <summary>
/// Represents a stack-only decoded PCM frame view for synchronous media loops.
/// </summary>
public readonly ref struct AudioFrameView
{
    /// <summary>Initializes a new instance of the <see cref="AudioFrameView"/> struct.</summary>
    public AudioFrameView(
        ReadOnlySpan<byte> data,
        AudioFormat format,
        int samplesPerChannel,
        long? sequenceNumber = null,
        TimeSpan? captureTime = null,
        DateTimeOffset? observedAt = null,
        AudioRecoveryKind recoveryKind = AudioRecoveryKind.None,
        AudioFrameFlags flags = AudioFrameFlags.None)
    {
        Data = data;
        Format = format;
        SamplesPerChannel = samplesPerChannel;
        SequenceNumber = sequenceNumber;
        CaptureTime = captureTime;
        ObservedAt = observedAt;
        RecoveryKind = recoveryKind;
        Flags = flags;
    }

    /// <summary>Gets PCM bytes in little-endian, interleaved channel order.</summary>
    public ReadOnlySpan<byte> Data { get; }

    /// <summary>Gets the format of the PCM bytes.</summary>
    public AudioFormat Format { get; }

    /// <summary>Gets the number of samples per channel in this frame.</summary>
    public int SamplesPerChannel { get; }

    /// <summary>Gets an optional stream-local sequence number.</summary>
    public long? SequenceNumber { get; }

    /// <summary>Gets an optional capture timestamp relative to the producing stream.</summary>
    public TimeSpan? CaptureTime { get; }

    /// <summary>Gets an optional receive or production timestamp.</summary>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary>Gets the recovery status for this frame.</summary>
    public AudioRecoveryKind RecoveryKind { get; }

    /// <summary>Gets typed hot-path flags.</summary>
    public AudioFrameFlags Flags { get; }

    /// <summary>Gets the duration represented by this frame.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)SamplesPerChannel / Format.SampleRate);
}

/// <summary>
/// Transfers ownership of a retained audio frame backed by leased memory.
/// </summary>
public readonly struct OwnedAudioFrame : IDisposable
{
    /// <summary>Gets the retained audio frame.</summary>
    public required AudioFrame Frame { get; init; }

    /// <summary>Gets the lease that owns the memory referenced by the frame.</summary>
    public required IByteBufferLease Lease { get; init; }

    /// <summary>Releases the owned frame memory.</summary>
    public void Dispose() => Lease.Dispose();
}

/// <summary>
/// Identifies an optional non-hot audio-frame metadata value.
/// </summary>
public readonly struct AudioFrameMetadataKey
{
    /// <summary>Gets the stable metadata namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the stable metadata name.</summary>
    public required string Name { get; init; }
}

/// <summary>
/// Reads optional extension metadata outside packet/frame hot loops.
/// </summary>
public interface IAudioFrameMetadataProvider
{
    /// <summary>Attempts to format a metadata value into a caller-provided buffer.</summary>
    bool TryFormat(AudioFrameMetadataKey key, Span<char> destination, out int charsWritten);
}

/// <summary>
/// Wraps a retained audio frame with non-hot metadata.
/// </summary>
public readonly struct AudioFrameEnvelope
{
    /// <summary>Gets the retained audio frame.</summary>
    public required AudioFrame Frame { get; init; }

    /// <summary>Gets optional non-hot metadata.</summary>
    public IAudioFrameMetadataProvider? Metadata { get; init; }
}

/// <summary>
/// Describes the observable state of an audio source.
/// </summary>
public enum AudioSourceState
{
    Open = 0,
    Completed = 1,
    Failed = 2,
    Disposed = 3
}

/// <summary>
/// Describes the observable state of an audio sink.
/// </summary>
public enum AudioSinkState
{
    Open = 0,
    Completing = 1,
    Completed = 2,
    Failed = 3,
    Disposed = 4
}

/// <summary>
/// Classifies audio stream failures.
/// </summary>
public enum AudioStreamErrorKind
{
    RemoteClosed = 0,
    TransportFailure = 1,
    ProtocolError = 2,
    FormatMismatch = 3,
    BackpressureOverflow = 4,
    AlreadyCompleted = 5,
    Disposed = 6,
    BufferTooSmall = 7,
    Unknown = 8
}

/// <summary>
/// Base exception for audio stream source and sink failures.
/// </summary>
public abstract class AudioStreamException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AudioStreamException"/> class.</summary>
    protected AudioStreamException(AudioStreamErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>Gets the failure classification.</summary>
    public AudioStreamErrorKind Kind { get; }
}

/// <summary>
/// Represents a failure while reading from an audio source.
/// </summary>
public sealed class AudioSourceException : AudioStreamException
{
    /// <summary>Initializes a new instance of the <see cref="AudioSourceException"/> class.</summary>
    public AudioSourceException(AudioStreamErrorKind kind, string message, Exception? innerException = null)
        : base(kind, message, innerException)
    {
    }
}

/// <summary>
/// Represents a failure while writing to an audio sink.
/// </summary>
public sealed class AudioSinkException : AudioStreamException
{
    /// <summary>Initializes a new instance of the <see cref="AudioSinkException"/> class.</summary>
    public AudioSinkException(AudioStreamErrorKind kind, string message, Exception? innerException = null)
        : base(kind, message, innerException)
    {
    }
}

/// <summary>
/// Represents the result of reading from an audio source.
/// </summary>
public readonly struct AudioReadResult
{
    /// <summary>Gets a value indicating whether a frame was read.</summary>
    public required bool HasFrame { get; init; }

    /// <summary>Gets the frame when <see cref="HasFrame"/> is true.</summary>
    public AudioFrame Frame { get; init; }

    /// <summary>Gets a value indicating whether the source completed.</summary>
    public bool IsCompleted => !HasFrame;
}

/// <summary>
/// Receives decoded audio frames without requiring per-frame collection allocation.
/// </summary>
public interface IAudioFrameSink
{
    /// <summary>Attempts to accept a decoded audio frame.</summary>
    bool TryWrite(in AudioFrame frame);
}

/// <summary>
/// Receives stack-only decoded audio frame views.
/// </summary>
public interface IAudioFrameViewSink
{
    /// <summary>Attempts to accept a decoded audio frame view.</summary>
    bool TryWrite(in AudioFrameView frame);
}

/// <summary>
/// Reads already-available decoded audio frames without awaiting or allocating.
/// </summary>
public interface IAudioFrameReader
{
    /// <summary>Attempts to read an immediately available decoded audio frame.</summary>
    bool TryRead(out AudioFrame frame);
}

/// <summary>
/// Provides a pull-backpressured stream of decoded audio frames.
/// </summary>
public interface IAudioSource : IAsyncDisposable, IAudioFrameReader
{
    /// <summary>Gets the normal output format for frames produced by this source.</summary>
    AudioFormat Format { get; }

    /// <summary>Gets a value indicating whether frames may change format during the stream.</summary>
    bool CanChangeFormat { get; }

    /// <summary>Gets the current source state.</summary>
    AudioSourceState State { get; }

    /// <summary>Reads one decoded frame or completion result.</summary>
    ValueTask<AudioReadResult> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides an ergonomic async-enumerable facade over an audio source.
/// </summary>
public interface IAsyncAudioFrameSource : IAudioSource
{
    /// <summary>Reads decoded frames in chronological order until the source completes, fails, or is canceled.</summary>
    IAsyncEnumerable<AudioFrame> ReadFramesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumes decoded audio frames with push backpressure.
/// </summary>
public interface IAudioSink : IAsyncDisposable, IAudioFrameSink
{
    /// <summary>Gets the preferred input format, or null when the sink accepts multiple formats.</summary>
    AudioFormat? PreferredFormat { get; }

    /// <summary>Gets the current sink state.</summary>
    AudioSinkState State { get; }

    /// <summary>Accepts a frame for output.</summary>
    ValueTask WriteAsync(AudioFrame frame, CancellationToken cancellationToken = default);

    /// <summary>Drains frames accepted before the flush call while leaving the sink open.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Drains accepted frames and permanently completes output.</summary>
    ValueTask CompleteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pumps a bounded amount of realtime media work without using async state machines in the inner loop.
/// </summary>
public interface IRealtimeMediaPump
{
    /// <summary>Processes up to <paramref name="maxOperations"/> packets or frames using caller-provided scratch memory.</summary>
    int Pump(Span<byte> scratch, int maxOperations);
}
