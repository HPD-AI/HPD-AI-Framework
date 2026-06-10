#nullable enable

using System.Buffers;
using HPD.Audio.Primitives;
using HPD.Buffers;

namespace HPD.Audio.Codecs;

/// <summary>
/// Identifies an encoded audio representation.
/// </summary>
public enum AudioEncoding
{
    Pcm16 = 1,
    Opus = 2,
    Pcmu = 3,
    Pcma = 4,
    G722 = 5
}

/// <summary>
/// Identifies an encoded-format parameter without requiring a hot-path dictionary.
/// </summary>
public enum EncodedAudioParameter
{
    Unknown = 0,
    OpusUseInBandFec = 1,
    OpusDtx = 2,
    OpusMaxPlaybackRate = 3,
    OpusStereo = 4,
    PacketTimeMilliseconds = 5,
    MaxPacketTimeMilliseconds = 6
}

/// <summary>
/// Represents one typed encoded-format parameter.
/// </summary>
public readonly struct EncodedAudioFormatParameter
{
    /// <summary>Gets the parameter kind.</summary>
    public required EncodedAudioParameter Parameter { get; init; }

    /// <summary>Gets the integer value when the parameter is integer-valued.</summary>
    public int Int32Value { get; init; }

    /// <summary>Gets the boolean value when the parameter is boolean-valued.</summary>
    public bool BooleanValue { get; init; }
}

/// <summary>
/// Reads encoded audio format parameters without requiring dictionary allocation.
/// </summary>
public interface IEncodedAudioFormatParameters
{
    /// <summary>Attempts to read a typed parameter.</summary>
    bool TryGet(EncodedAudioParameter parameter, out EncodedAudioFormatParameter value);
}

/// <summary>
/// Describes encoded audio after negotiation or container parsing.
/// </summary>
public readonly struct EncodedAudioFormat
{
    /// <summary>Gets the encoded audio representation.</summary>
    public required AudioEncoding Encoding { get; init; }

    /// <summary>Gets the nominal decoded sample rate.</summary>
    public required int SampleRate { get; init; }

    /// <summary>Gets the nominal decoded channel count.</summary>
    public required int ChannelCount { get; init; }

    /// <summary>Gets the RTP clock rate when the encoded audio came from RTP.</summary>
    public int? RtpClockRate { get; init; }

    /// <summary>Gets optional typed codec parameters.</summary>
    public IEncodedAudioFormatParameters? Parameters { get; init; }
}

/// <summary>
/// Represents one encoded audio access unit.
/// </summary>
public readonly struct EncodedAudioFrame
{
    /// <summary>Gets the encoded audio format.</summary>
    public required EncodedAudioFormat Format { get; init; }

    /// <summary>Gets the encoded access-unit bytes.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>Gets the media duration represented by this access unit.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the originating RTP timestamp when available.</summary>
    public uint? RtpTimestamp { get; init; }

    /// <summary>Gets the originating RTP sequence number when available.</summary>
    public ushort? RtpSequenceNumber { get; init; }
}

/// <summary>
/// Transfers ownership of an encoded audio frame backed by leased memory.
/// </summary>
public readonly struct OwnedEncodedAudioFrame : IDisposable
{
    /// <summary>Gets the retained encoded audio frame.</summary>
    public required EncodedAudioFrame Frame { get; init; }

    /// <summary>Gets the lease that owns the memory referenced by the frame.</summary>
    public required IByteBufferLease Lease { get; init; }

    /// <summary>Releases the owned encoded frame memory.</summary>
    public void Dispose() => Lease.Dispose();
}

/// <summary>
/// Represents a stack-only encoded audio access-unit view for synchronous codec loops.
/// </summary>
public readonly ref struct EncodedAudioFrameView
{
    /// <summary>Initializes a new instance of the <see cref="EncodedAudioFrameView"/> struct.</summary>
    public EncodedAudioFrameView(
        EncodedAudioFormat format,
        ReadOnlySpan<byte> data,
        TimeSpan duration,
        uint? rtpTimestamp = null,
        ushort? rtpSequenceNumber = null)
    {
        Format = format;
        Data = data;
        Duration = duration;
        RtpTimestamp = rtpTimestamp;
        RtpSequenceNumber = rtpSequenceNumber;
    }

    /// <summary>Gets the encoded audio format.</summary>
    public EncodedAudioFormat Format { get; }

    /// <summary>Gets the encoded access-unit bytes.</summary>
    public ReadOnlySpan<byte> Data { get; }

    /// <summary>Gets the media duration represented by this access unit.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the originating RTP timestamp when available.</summary>
    public uint? RtpTimestamp { get; }

    /// <summary>Gets the originating RTP sequence number when available.</summary>
    public ushort? RtpSequenceNumber { get; }
}

/// <summary>
/// Selects the decode operation to perform for a codec input.
/// </summary>
public enum DecodeMode
{
    Primary = 0,
    ConcealLoss = 1,
    RecoverPreviousFromFec = 2
}

/// <summary>
/// Classifies synchronous codec operation results without using exceptions for normal flow.
/// </summary>
public enum AudioCodecStatus
{
    Success = 0,
    UnsupportedFormat = 1,
    InvalidInput = 2,
    DestinationTooSmall = 3,
    SinkBackpressure = 4,
    Disposed = 5
}

/// <summary>
/// Carries one decode operation into an audio decoder.
/// </summary>
public readonly struct AudioDecodeInput
{
    /// <summary>Gets the encoded audio format.</summary>
    public required EncodedAudioFormat Format { get; init; }

    /// <summary>Gets the media duration to decode or synthesize.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the decode mode.</summary>
    public required DecodeMode Mode { get; init; }

    /// <summary>Gets the encoded payload; empty for pure loss concealment.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Gets the associated RTP timestamp when available.</summary>
    public uint? RtpTimestamp { get; init; }

    /// <summary>Gets the associated RTP sequence number when available.</summary>
    public ushort? RtpSequenceNumber { get; init; }

    /// <summary>Gets a value indicating whether in-band FEC was negotiated for this stream.</summary>
    public bool InBandFecNegotiated { get; init; }
}

/// <summary>
/// Carries one stack-only decode operation into an audio decoder.
/// </summary>
public readonly ref struct AudioDecodeInputView
{
    /// <summary>Initializes a new instance of the <see cref="AudioDecodeInputView"/> struct.</summary>
    public AudioDecodeInputView(
        EncodedAudioFormat format,
        TimeSpan duration,
        DecodeMode mode,
        ReadOnlySpan<byte> payload,
        uint? rtpTimestamp = null,
        ushort? rtpSequenceNumber = null,
        bool inBandFecNegotiated = false)
    {
        Format = format;
        Duration = duration;
        Mode = mode;
        Payload = payload;
        RtpTimestamp = rtpTimestamp;
        RtpSequenceNumber = rtpSequenceNumber;
        InBandFecNegotiated = inBandFecNegotiated;
    }

    /// <summary>Gets the encoded audio format.</summary>
    public EncodedAudioFormat Format { get; }

    /// <summary>Gets the media duration to decode or synthesize.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the decode mode.</summary>
    public DecodeMode Mode { get; }

    /// <summary>Gets the encoded payload; empty for pure loss concealment.</summary>
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>Gets the associated RTP timestamp when available.</summary>
    public uint? RtpTimestamp { get; }

    /// <summary>Gets the associated RTP sequence number when available.</summary>
    public ushort? RtpSequenceNumber { get; }

    /// <summary>Gets a value indicating whether in-band FEC was negotiated for this stream.</summary>
    public bool InBandFecNegotiated { get; }
}

/// <summary>
/// Receives encoded audio frames without requiring per-call collection allocation.
/// </summary>
public interface IEncodedAudioFrameSink
{
    /// <summary>Attempts to accept an encoded audio frame.</summary>
    bool TryWrite(in EncodedAudioFrame frame);
}

/// <summary>
/// Receives stack-only encoded audio frame views.
/// </summary>
public interface IEncodedAudioFrameViewSink
{
    /// <summary>Attempts to accept an encoded audio frame view.</summary>
    bool TryWrite(in EncodedAudioFrameView frame);
}

/// <summary>
/// Decodes encoded audio access units into decoded PCM frames.
/// </summary>
public interface IAudioDecoder : IAsyncDisposable
{
    /// <summary>Gets the decoder output format.</summary>
    AudioFormat OutputFormat { get; }

    /// <summary>Decodes one operation and writes zero or more PCM frames to <paramref name="sink"/>.</summary>
    AudioCodecStatus Decode(in AudioDecodeInput input, IAudioFrameSink sink);
}

/// <summary>
/// Decodes encoded audio access-unit views into decoded PCM frame views for realtime loops.
/// </summary>
public interface IRealtimeAudioDecoder
{
    /// <summary>Gets the decoder output format.</summary>
    AudioFormat OutputFormat { get; }

    /// <summary>Decodes one stack-only operation and writes zero or more PCM frame views to <paramref name="sink"/>.</summary>
    AudioCodecStatus Decode(in AudioDecodeInputView input, IAudioFrameViewSink sink);
}

/// <summary>
/// Encodes decoded PCM frames into encoded audio access units.
/// </summary>
public interface IAudioEncoder : IAsyncDisposable
{
    /// <summary>Gets the encoder input format.</summary>
    AudioFormat InputFormat { get; }

    /// <summary>Gets the encoder output format.</summary>
    EncodedAudioFormat OutputFormat { get; }

    /// <summary>Encodes one PCM frame and writes zero or more encoded access units to <paramref name="sink"/>.</summary>
    AudioCodecStatus Encode(in AudioFrame frame, IEncodedAudioFrameSink sink);
}

/// <summary>
/// Encodes decoded PCM frame views into encoded access-unit views for realtime loops.
/// </summary>
public interface IRealtimeAudioEncoder
{
    /// <summary>Gets the encoder input format.</summary>
    AudioFormat InputFormat { get; }

    /// <summary>Gets the encoder output format.</summary>
    EncodedAudioFormat OutputFormat { get; }

    /// <summary>Encodes one PCM frame view and writes zero or more encoded access-unit views to <paramref name="sink"/>.</summary>
    AudioCodecStatus Encode(in AudioFrameView frame, IEncodedAudioFrameViewSink sink);
}

/// <summary>
/// Optional buffer-oriented encoder for codecs that can encode directly into caller-owned storage.
/// </summary>
public interface IBufferAudioEncoder : IAudioEncoder
{
    /// <summary>Attempts to encode one PCM frame into a caller-provided writer.</summary>
    AudioCodecStatus TryEncode(in AudioFrame frame, IBufferWriter<byte> destination, out EncodedAudioFrame encodedFrame);
}

/// <summary>
/// Creates codecs through explicit typed construction rather than reflection-based activation.
/// </summary>
public interface IAudioCodecFactory
{
    /// <summary>Attempts to create a decoder for an encoded format.</summary>
    bool TryCreateDecoder(in EncodedAudioFormat format, out IAudioDecoder decoder);

    /// <summary>Attempts to create an encoder for input and output formats.</summary>
    bool TryCreateEncoder(in AudioFormat inputFormat, in EncodedAudioFormat outputFormat, out IAudioEncoder encoder);
}
