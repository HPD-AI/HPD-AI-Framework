#nullable enable

using HPD.Audio.Codecs;
using HPD.Audio.Primitives;
using HPD.Media.Rtp;
using HPD.Media.Rtp.Audio;
using HPD.Media.Transport;

namespace HPD.Audio.WebRTC;

/// <summary>
/// Classifies WebRTC RTP audio outbound processing results without exceptions for packet flow.
/// </summary>
public enum WebRtcAudioOutboundStatus
{
    /// <summary>The frame was accepted and any produced packets were written.</summary>
    Success = 0,

    /// <summary>The RTP payload map changed and the caller must create a new pump or packet boundary.</summary>
    FormatMapChanged = 1,

    /// <summary>The configured encoder output does not match the payload type binding.</summary>
    UnsupportedFormat = 2,

    /// <summary>The source frame was invalid for the configured encoder.</summary>
    InvalidFrame = 3,

    /// <summary>The configured encoder rejected the frame.</summary>
    EncodeFailed = 4,

    /// <summary>The caller-provided packet scratch buffer was too small.</summary>
    DestinationTooSmall = 5,

    /// <summary>The packet protector rejected the RTP packet.</summary>
    PacketProtectionFailed = 6,

    /// <summary>The protected packet sink could not accept output.</summary>
    SinkBackpressure = 7
}

/// <summary>
/// Receives protected RTP-family packets without requiring per-packet allocation.
/// </summary>
public interface IWebRtcProtectedPacketSink
{
    /// <summary>Attempts to accept one protected packet.</summary>
    bool TryWrite(ReadOnlySpan<byte> packet);
}

/// <summary>
/// Moves one outbound PCM frame through realtime encode, RTP write, in-place SRTP protect, and protected packet output.
/// </summary>
public sealed class WebRtcAudioOutboundPump
{
    private readonly IRealtimeAudioEncoder encoder;
    private readonly IRtpAudioFormatMap formatMap;
    private readonly IPacketProtector rtpProtector;
    private readonly IWebRtcProtectedPacketSink sink;
    private readonly uint ssrc;
    private readonly byte payloadType;
    private readonly EncodedSink encodedSink;
    private ulong formatMapVersion;
    private ushort nextSequenceNumber;
    private uint nextTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcAudioOutboundPump"/> class.
    /// </summary>
    public WebRtcAudioOutboundPump(
        IRealtimeAudioEncoder encoder,
        IRtpAudioFormatMap formatMap,
        IPacketProtector rtpProtector,
        IWebRtcProtectedPacketSink sink,
        uint ssrc,
        byte payloadType,
        ushort initialSequenceNumber = 0,
        uint initialTimestamp = 0)
    {
        this.encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        this.formatMap = formatMap ?? throw new ArgumentNullException(nameof(formatMap));
        this.rtpProtector = rtpProtector ?? throw new ArgumentNullException(nameof(rtpProtector));
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.ssrc = ssrc;
        this.payloadType = payloadType;
        nextSequenceNumber = initialSequenceNumber;
        nextTimestamp = initialTimestamp;
        formatMapVersion = formatMap.Version;
        encodedSink = new EncodedSink(this);
    }

    /// <summary>
    /// Encodes and protects one outbound PCM frame using caller-provided packet scratch storage.
    /// </summary>
    public WebRtcAudioOutboundStatus ProcessFrame(in AudioFrameView frame, Memory<byte> packetScratch)
    {
        if (formatMap.Version != formatMapVersion)
        {
            return WebRtcAudioOutboundStatus.FormatMapChanged;
        }

        if (payloadType > 127 ||
            !formatMap.TryGetFormat(payloadType, out RtpAudioFormatBinding binding) ||
            !FormatsMatch(encoder.OutputFormat, binding.EncodedFormat))
        {
            return WebRtcAudioOutboundStatus.UnsupportedFormat;
        }

        if (!FormatsMatch(frame.Format, encoder.InputFormat) || frame.Data.IsEmpty || frame.SamplesPerChannel <= 0)
        {
            return WebRtcAudioOutboundStatus.InvalidFrame;
        }

        encodedSink.Reset(packetScratch, binding);
        AudioCodecStatus encodeStatus = encoder.Encode(frame, encodedSink);
        if (encodedSink.Status != WebRtcAudioOutboundStatus.Success)
        {
            return encodedSink.Status;
        }

        if (encodeStatus == AudioCodecStatus.SinkBackpressure)
        {
            return WebRtcAudioOutboundStatus.SinkBackpressure;
        }

        if (encodeStatus is AudioCodecStatus.InvalidInput or AudioCodecStatus.UnsupportedFormat)
        {
            return WebRtcAudioOutboundStatus.InvalidFrame;
        }

        if (encodeStatus != AudioCodecStatus.Success)
        {
            return WebRtcAudioOutboundStatus.EncodeFailed;
        }

        return encodedSink.Status;
    }

    private WebRtcAudioOutboundStatus WriteEncodedFrame(in EncodedAudioFrameView frame, Memory<byte> packetScratch, in RtpAudioFormatBinding binding)
    {
        if (!FormatsMatch(frame.Format, binding.EncodedFormat))
        {
            return WebRtcAudioOutboundStatus.UnsupportedFormat;
        }

        TimeSpan duration = frame.Duration;
        if (duration <= TimeSpan.Zero)
        {
            return WebRtcAudioOutboundStatus.InvalidFrame;
        }

        if (!TryConvertDurationToTimestampUnits(duration, binding.EncodedFormat, out uint timestampUnits))
        {
            return WebRtcAudioOutboundStatus.InvalidFrame;
        }

        ushort sequenceNumber = frame.RtpSequenceNumber ?? nextSequenceNumber;
        uint timestamp = frame.RtpTimestamp ?? nextTimestamp;
        var header = new RtpHeader
        {
            PayloadType = payloadType,
            SequenceNumber = sequenceNumber,
            Timestamp = timestamp,
            Ssrc = ssrc
        };

        RtpPacketStatus writeStatus = RtpPacketWriter.TryWrite(
            header,
            ReadOnlySpan<uint>.Empty,
            frame.Data,
            ReadOnlySpan<byte>.Empty,
            packetScratch.Span,
            out int rtpLength);
        if (writeStatus == RtpPacketStatus.DestinationTooSmall)
        {
            return WebRtcAudioOutboundStatus.DestinationTooSmall;
        }

        if (writeStatus != RtpPacketStatus.Success)
        {
            return WebRtcAudioOutboundStatus.InvalidFrame;
        }

        PacketProtectionStatus protectStatus = rtpProtector.Protect(packetScratch.Span, rtpLength, out int protectedLength);
        if (protectStatus == PacketProtectionStatus.DestinationTooSmall)
        {
            return WebRtcAudioOutboundStatus.DestinationTooSmall;
        }

        if (protectStatus != PacketProtectionStatus.Success)
        {
            return WebRtcAudioOutboundStatus.PacketProtectionFailed;
        }

        if (!sink.TryWrite(packetScratch.Span[..protectedLength]))
        {
            return WebRtcAudioOutboundStatus.SinkBackpressure;
        }

        nextSequenceNumber = (ushort)(sequenceNumber + 1);
        nextTimestamp = timestamp + timestampUnits;
        return WebRtcAudioOutboundStatus.Success;
    }

    private static bool FormatsMatch(in EncodedAudioFormat actual, in EncodedAudioFormat expected)
    {
        return actual.Encoding == expected.Encoding &&
            actual.SampleRate == expected.SampleRate &&
            actual.ChannelCount == expected.ChannelCount &&
            (actual.RtpClockRate ?? actual.SampleRate) == (expected.RtpClockRate ?? expected.SampleRate);
    }

    private static bool FormatsMatch(in AudioFormat actual, in AudioFormat expected)
    {
        return actual.SampleFormat == expected.SampleFormat &&
            actual.SampleRate == expected.SampleRate &&
            actual.ChannelCount == expected.ChannelCount;
    }

    private static bool TryConvertDurationToTimestampUnits(
        TimeSpan duration,
        in EncodedAudioFormat format,
        out uint timestampUnits)
    {
        timestampUnits = 0;
        int clockRate = format.RtpClockRate ?? format.SampleRate;
        if (duration <= TimeSpan.Zero || clockRate <= 0)
        {
            return false;
        }

        double units = duration.TotalSeconds * clockRate;
        if (units < 0.5 || units > uint.MaxValue)
        {
            return false;
        }

        timestampUnits = (uint)Math.Round(units);
        return timestampUnits != 0;
    }

    private sealed class EncodedSink(WebRtcAudioOutboundPump pump) : IEncodedAudioFrameViewSink
    {
        private Memory<byte> packetScratch;
        private RtpAudioFormatBinding binding;

        public WebRtcAudioOutboundStatus Status { get; private set; }

        public void Reset(Memory<byte> packetScratch, in RtpAudioFormatBinding binding)
        {
            this.packetScratch = packetScratch;
            this.binding = binding;
            Status = WebRtcAudioOutboundStatus.Success;
        }

        public bool TryWrite(in EncodedAudioFrameView frame)
        {
            if (Status != WebRtcAudioOutboundStatus.Success)
            {
                return false;
            }

            Status = pump.WriteEncodedFrame(frame, packetScratch, binding);
            return Status == WebRtcAudioOutboundStatus.Success;
        }
    }
}
