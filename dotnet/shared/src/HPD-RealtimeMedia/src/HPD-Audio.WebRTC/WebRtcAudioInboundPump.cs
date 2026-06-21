#nullable enable

using HPD.Audio.Codecs;
using HPD.Audio.Primitives;
using HPD.Media.Rtp;
using HPD.Media.Rtp.Audio;
using HPD.Media.Transport;

namespace HPD.Audio.WebRTC;

/// <summary>
/// Classifies WebRTC RTP audio inbound processing results without exceptions for packet flow.
/// </summary>
public enum WebRtcAudioInboundStatus
{
    /// <summary>The packet was accepted and decoded.</summary>
    Success = 0,

    /// <summary>The packet protector rejected the protected packet.</summary>
    PacketProtectionFailed = 1,

    /// <summary>The packet was not valid RTP audio.</summary>
    InvalidRtpPacket = 2,

    /// <summary>The RTP payload type was not present in the audio payload map.</summary>
    UnknownPayloadType = 3,

    /// <summary>The resolved RTP audio format cannot be decoded by this pump.</summary>
    UnsupportedFormat = 4,

    /// <summary>The configured decoder rejected the access unit.</summary>
    DecodeFailed = 5,

    /// <summary>The downstream PCM sink could not accept decoded output.</summary>
    SinkBackpressure = 6
}

/// <summary>
/// Moves one inbound WebRTC SRTP/RTP audio packet through unprotect, RTP parse, payload-map resolution, and realtime decode.
/// </summary>
public sealed class WebRtcAudioInboundPump
{
    private readonly IPacketProtector rtpProtector;
    private readonly IRtpAudioFormatMap formatMap;
    private readonly IRealtimeAudioDecoder decoder;
    private readonly IAudioFrameViewSink sink;
    private bool hasPreviousPacket;
    private uint previousSsrc;
    private uint previousTimestamp;
    private TimeSpan previousDuration;
    private ulong formatMapVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcAudioInboundPump"/> class.
    /// </summary>
    public WebRtcAudioInboundPump(
        IPacketProtector rtpProtector,
        IRtpAudioFormatMap formatMap,
        IRealtimeAudioDecoder decoder,
        IAudioFrameViewSink sink)
    {
        this.rtpProtector = rtpProtector ?? throw new ArgumentNullException(nameof(rtpProtector));
        this.formatMap = formatMap ?? throw new ArgumentNullException(nameof(formatMap));
        this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        formatMapVersion = formatMap.Version;
    }

    /// <summary>
    /// Unprotects and decodes one complete inbound RTP packet in place.
    /// </summary>
    public WebRtcAudioInboundStatus ProcessPacket(Span<byte> packet, int inputLength)
    {
        if ((uint)inputLength > (uint)packet.Length)
        {
            return WebRtcAudioInboundStatus.InvalidRtpPacket;
        }

        if (formatMap.Version != formatMapVersion)
        {
            formatMapVersion = formatMap.Version;
            hasPreviousPacket = false;
            previousSsrc = 0;
            previousTimestamp = 0;
            previousDuration = TimeSpan.Zero;
        }

        PacketProtectionStatus protectionStatus = rtpProtector.Unprotect(packet, inputLength, out int unprotectedLength);
        if (protectionStatus != PacketProtectionStatus.Success)
        {
            return WebRtcAudioInboundStatus.PacketProtectionFailed;
        }

        RtpPacketStatus rtpStatus = RtpPacketReader.TryParse(packet[..unprotectedLength], out RtpPacketView view);
        if (rtpStatus != RtpPacketStatus.Success)
        {
            return WebRtcAudioInboundStatus.InvalidRtpPacket;
        }

        if (view.Header.PayloadType > 127 ||
            !formatMap.TryGetFormat(view.Header.PayloadType, out RtpAudioFormatBinding binding))
        {
            return WebRtcAudioInboundStatus.UnknownPayloadType;
        }

        if (!IsFormatUsable(binding.EncodedFormat) ||
            !FormatsMatch(decoder.OutputFormat, binding.EncodedFormat))
        {
            return WebRtcAudioInboundStatus.UnsupportedFormat;
        }

        TimeSpan duration = ResolveDuration(view.Header, binding);
        if (duration <= TimeSpan.Zero)
        {
            return WebRtcAudioInboundStatus.InvalidRtpPacket;
        }

        var input = new AudioDecodeInputView(
            binding.EncodedFormat,
            duration,
            DecodeMode.Primary,
            view.Payload,
            view.Header.Timestamp,
            view.Header.SequenceNumber);

        AudioCodecStatus decodeStatus = decoder.Decode(input, sink);
        if (decodeStatus == AudioCodecStatus.SinkBackpressure)
        {
            return WebRtcAudioInboundStatus.SinkBackpressure;
        }

        if (decodeStatus != AudioCodecStatus.Success)
        {
            return WebRtcAudioInboundStatus.DecodeFailed;
        }

        hasPreviousPacket = true;
        previousSsrc = view.Header.Ssrc;
        previousTimestamp = view.Header.Timestamp;
        previousDuration = duration;
        return WebRtcAudioInboundStatus.Success;
    }

    private TimeSpan ResolveDuration(in RtpHeader header, in RtpAudioFormatBinding binding)
    {
        TimeSpan duration = binding.DefaultPacketTime ?? TimeSpan.Zero;
        if (hasPreviousPacket && header.Ssrc == previousSsrc && header.Timestamp != previousTimestamp)
        {
            int clockRate = binding.EncodedFormat.RtpClockRate ?? binding.EncodedFormat.SampleRate;
            uint timestampDelta = header.Timestamp - previousTimestamp;
            duration = TimeSpan.FromSeconds((double)timestampDelta / clockRate);
        }

        if (duration == TimeSpan.Zero)
        {
            duration = previousDuration;
        }

        return duration;
    }

    private static bool IsFormatUsable(in EncodedAudioFormat format)
    {
        return format.SampleRate > 0 &&
            format.ChannelCount > 0 &&
            (format.RtpClockRate ?? format.SampleRate) > 0;
    }

    private static bool FormatsMatch(in AudioFormat outputFormat, in EncodedAudioFormat encodedFormat)
    {
        return outputFormat.SampleFormat == AudioSampleFormat.Pcm16 &&
            outputFormat.SampleRate == encodedFormat.SampleRate &&
            outputFormat.ChannelCount == encodedFormat.ChannelCount;
    }
}
