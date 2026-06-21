#nullable enable

using HPD.Audio.Codecs;
using HPD.Audio.Primitives;
using HPD.Audio.WebRTC;
using HPD.Media.Rtp;
using HPD.Media.Rtp.Audio;
using HPD.Media.Transport;

namespace HPD.Audio.WebRTC.Tests.Pipeline;

public sealed class WebRtcAudioInboundPumpTests
{
    private static readonly EncodedAudioFormat PcmuFormat = new()
    {
        Encoding = AudioEncoding.Pcmu,
        SampleRate = 8000,
        ChannelCount = 1,
        RtpClockRate = 8000
    };

    private static readonly AudioFormat PcmFormat = new()
    {
        SampleRate = 8000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16
    };

    [Fact]
    public void ProcessPacket_UnprotectsParsesAndDecodesIntoSink()
    {
        var formatMap = new RtpAudioFormatMap(
            1,
            [
                new RtpAudioFormatBinding
                {
                    PayloadType = 0,
                    EncodedFormat = PcmuFormat,
                    DefaultPacketTime = TimeSpan.FromMilliseconds(20)
                }
            ]);
        var decoder = new CapturingRealtimeDecoder(PcmFormat);
        var sink = new CapturingFrameViewSink();
        var pump = new WebRtcAudioInboundPump(new NoOpPacketProtector(), formatMap, decoder, sink);

        Span<byte> packet = stackalloc byte[64];
        int length = WriteRtpPacket(packet, sequenceNumber: 123, timestamp: 160, payload: [0x7F, 0x80, 0x81]);

        WebRtcAudioInboundStatus status = pump.ProcessPacket(packet, length);

        Assert.Equal(WebRtcAudioInboundStatus.Success, status);
        Assert.Equal(1, decoder.DecodeCount);
        Assert.Equal(TimeSpan.FromMilliseconds(20), decoder.LastDuration);
        Assert.Equal((ushort)123, decoder.LastSequenceNumber);
        Assert.Equal(160u, decoder.LastTimestamp);
        Assert.Equal(3, decoder.LastPayloadLength);
        Assert.Equal(1, sink.FrameCount);
        Assert.Equal(TimeSpan.FromMilliseconds(20), sink.LastDuration);
    }

    [Fact]
    public void ProcessPacket_UsesTimestampDeltaAfterFirstPacket()
    {
        var formatMap = new RtpAudioFormatMap(
            1,
            [
                new RtpAudioFormatBinding
                {
                    PayloadType = 0,
                    EncodedFormat = PcmuFormat,
                    DefaultPacketTime = TimeSpan.FromMilliseconds(10)
                }
            ]);
        var decoder = new CapturingRealtimeDecoder(PcmFormat);
        var sink = new CapturingFrameViewSink();
        var pump = new WebRtcAudioInboundPump(new NoOpPacketProtector(), formatMap, decoder, sink);

        Span<byte> first = stackalloc byte[64];
        Span<byte> second = stackalloc byte[64];
        int firstLength = WriteRtpPacket(first, sequenceNumber: 1, timestamp: 1000, payload: [0x01]);
        int secondLength = WriteRtpPacket(second, sequenceNumber: 2, timestamp: 1160, payload: [0x02]);

        Assert.Equal(WebRtcAudioInboundStatus.Success, pump.ProcessPacket(first, firstLength));
        Assert.Equal(WebRtcAudioInboundStatus.Success, pump.ProcessPacket(second, secondLength));

        Assert.Equal(TimeSpan.FromMilliseconds(20), decoder.LastDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(20), sink.LastDuration);
    }

    [Fact]
    public void ProcessPacket_ReturnsUnknownPayloadTypeForUnmappedPacket()
    {
        var formatMap = new RtpAudioFormatMap(1, []);
        var pump = new WebRtcAudioInboundPump(
            new NoOpPacketProtector(),
            formatMap,
            new CapturingRealtimeDecoder(PcmFormat),
            new CapturingFrameViewSink());

        Span<byte> packet = stackalloc byte[64];
        int length = WriteRtpPacket(packet, sequenceNumber: 1, timestamp: 1, payloadType: 111, payload: [0x01]);

        Assert.Equal(WebRtcAudioInboundStatus.UnknownPayloadType, pump.ProcessPacket(packet, length));
    }

    [Fact]
    public void ProcessPacket_MapsProtectionFailure()
    {
        var formatMap = new RtpAudioFormatMap(
            1,
            [
                new RtpAudioFormatBinding
                {
                    PayloadType = 0,
                    EncodedFormat = PcmuFormat,
                    DefaultPacketTime = TimeSpan.FromMilliseconds(20)
                }
            ]);
        var pump = new WebRtcAudioInboundPump(
            new RejectingPacketProtector(),
            formatMap,
            new CapturingRealtimeDecoder(PcmFormat),
            new CapturingFrameViewSink());

        Span<byte> packet = stackalloc byte[64];
        int length = WriteRtpPacket(packet, sequenceNumber: 1, timestamp: 1, payload: [0x01]);

        Assert.Equal(WebRtcAudioInboundStatus.PacketProtectionFailed, pump.ProcessPacket(packet, length));
    }

    private static int WriteRtpPacket(
        Span<byte> destination,
        ushort sequenceNumber,
        uint timestamp,
        ReadOnlySpan<byte> payload,
        byte payloadType = 0)
    {
        byte[] payloadBytes = payload.ToArray();
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = payloadType,
                SequenceNumber = sequenceNumber,
                Timestamp = timestamp,
                Ssrc = 0x01020304
            },
            Payload = payloadBytes,
            ArrivalTime = DateTimeOffset.UtcNow
        };

        RtpPacketStatus status = RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten);
        Assert.Equal(RtpPacketStatus.Success, status);
        return bytesWritten;
    }

    private sealed class NoOpPacketProtector : IPacketProtector
    {
        public int MaximumExpansionBytes => 0;

        public int ProtectCount { get; private set; }

        public PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
        {
            ProtectCount++;
            outputLength = inputLength;
            return PacketProtectionStatus.Success;
        }

        public PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = inputLength;
            return PacketProtectionStatus.Success;
        }
    }

    private sealed class RejectingPacketProtector : IPacketProtector
    {
        public int MaximumExpansionBytes => 0;

        public PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = 0;
            return PacketProtectionStatus.AuthenticationFailed;
        }

        public PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = 0;
            return PacketProtectionStatus.AuthenticationFailed;
        }
    }

    private sealed class CapturingRealtimeDecoder(AudioFormat outputFormat) : IRealtimeAudioDecoder
    {
        public AudioFormat OutputFormat { get; } = outputFormat;

        public int DecodeCount { get; private set; }

        public TimeSpan LastDuration { get; private set; }

        public uint? LastTimestamp { get; private set; }

        public ushort? LastSequenceNumber { get; private set; }

        public int LastPayloadLength { get; private set; }

        public AudioCodecStatus Decode(in AudioDecodeInputView input, IAudioFrameViewSink sink)
        {
            DecodeCount++;
            LastDuration = input.Duration;
            LastTimestamp = input.RtpTimestamp;
            LastSequenceNumber = input.RtpSequenceNumber;
            LastPayloadLength = input.Payload.Length;
            Span<byte> pcm = stackalloc byte[320];
            return sink.TryWrite(new AudioFrameView(pcm, OutputFormat, 160))
                ? AudioCodecStatus.Success
                : AudioCodecStatus.SinkBackpressure;
        }
    }

    private sealed class CapturingFrameViewSink : IAudioFrameViewSink
    {
        public int FrameCount { get; private set; }

        public TimeSpan LastDuration { get; private set; }

        public bool TryWrite(in AudioFrameView frame)
        {
            FrameCount++;
            LastDuration = frame.Duration;
            return true;
        }
    }
}

public sealed class WebRtcAudioOutboundPumpTests
{
    private static readonly EncodedAudioFormat OpusFormat = new()
    {
        Encoding = AudioEncoding.Opus,
        SampleRate = 48000,
        ChannelCount = 1,
        RtpClockRate = 48000
    };

    private static readonly AudioFormat PcmFormat = new()
    {
        SampleRate = 48000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16
    };

    [Fact]
    public void ProcessFrame_EncodesWritesProtectsAndSinksPacket()
    {
        var formatMap = new RtpAudioFormatMap(
            1,
            [
                new RtpAudioFormatBinding
                {
                    PayloadType = 111,
                    EncodedFormat = OpusFormat,
                    DefaultPacketTime = TimeSpan.FromMilliseconds(20)
                }
            ]);
        var encoder = new CapturingRealtimeEncoder(PcmFormat, OpusFormat);
        var protector = new NoOpPacketProtector();
        var sink = new CapturingProtectedPacketSink();
        var pump = new WebRtcAudioOutboundPump(
            encoder,
            formatMap,
            protector,
            sink,
            ssrc: 0x01020304,
            payloadType: 111,
            initialSequenceNumber: 10,
            initialTimestamp: 960);
        byte[] pcm = new byte[1920];
        byte[] scratch = new byte[128];

        WebRtcAudioOutboundStatus status = pump.ProcessFrame(new AudioFrameView(pcm, PcmFormat, 960), scratch);

        Assert.Equal(WebRtcAudioOutboundStatus.Success, status);
        Assert.Equal(1, encoder.EncodeCount);
        Assert.Equal(1, protector.ProtectCount);
        Assert.Equal(1, sink.PacketCount);
        Assert.Equal(RtpPacketStatus.Success, RtpPacketReader.TryParse(sink.LastPacket, out RtpPacketView view));
        Assert.Equal(111, view.Header.PayloadType);
        Assert.Equal(10, view.Header.SequenceNumber);
        Assert.Equal(960u, view.Header.Timestamp);
        Assert.Equal(0x01020304u, view.Header.Ssrc);
        Assert.Equal([0x11, 0x22, 0x33], view.Payload.ToArray());
    }

    [Fact]
    public void ProcessFrame_AdvancesSequenceAndTimestamp()
    {
        var formatMap = new RtpAudioFormatMap(
            1,
            [
                new RtpAudioFormatBinding
                {
                    PayloadType = 111,
                    EncodedFormat = OpusFormat,
                    DefaultPacketTime = TimeSpan.FromMilliseconds(20)
                }
            ]);
        var sink = new CapturingProtectedPacketSink();
        var pump = new WebRtcAudioOutboundPump(
            new CapturingRealtimeEncoder(PcmFormat, OpusFormat),
            formatMap,
            new NoOpPacketProtector(),
            sink,
            ssrc: 0x01020304,
            payloadType: 111,
            initialSequenceNumber: 10,
            initialTimestamp: 960);
        byte[] pcm = new byte[1920];
        byte[] scratch = new byte[128];

        Assert.Equal(WebRtcAudioOutboundStatus.Success, pump.ProcessFrame(new AudioFrameView(pcm, PcmFormat, 960), scratch));
        Assert.Equal(WebRtcAudioOutboundStatus.Success, pump.ProcessFrame(new AudioFrameView(pcm, PcmFormat, 960), scratch));

        Assert.Equal(RtpPacketStatus.Success, RtpPacketReader.TryParse(sink.LastPacket, out RtpPacketView view));
        Assert.Equal(11, view.Header.SequenceNumber);
        Assert.Equal(1920u, view.Header.Timestamp);
    }

    [Fact]
    public void ProcessFrame_ReturnsDestinationTooSmallWhenScratchCannotFitProtectionExpansion()
    {
        var formatMap = new RtpAudioFormatMap(
            1,
            [
                new RtpAudioFormatBinding
                {
                    PayloadType = 111,
                    EncodedFormat = OpusFormat,
                    DefaultPacketTime = TimeSpan.FromMilliseconds(20)
                }
            ]);
        var pump = new WebRtcAudioOutboundPump(
            new CapturingRealtimeEncoder(PcmFormat, OpusFormat),
            formatMap,
            new ExpandingPacketProtector(expansionBytes: 4),
            new CapturingProtectedPacketSink(),
            ssrc: 0x01020304,
            payloadType: 111);
        byte[] pcm = new byte[1920];
        byte[] scratch = new byte[15];

        Assert.Equal(
            WebRtcAudioOutboundStatus.DestinationTooSmall,
            pump.ProcessFrame(new AudioFrameView(pcm, PcmFormat, 960), scratch));
    }

    private sealed class CapturingRealtimeEncoder(AudioFormat inputFormat, EncodedAudioFormat outputFormat) : IRealtimeAudioEncoder
    {
        public AudioFormat InputFormat { get; } = inputFormat;

        public EncodedAudioFormat OutputFormat { get; } = outputFormat;

        public int EncodeCount { get; private set; }

        public AudioCodecStatus Encode(in AudioFrameView frame, IEncodedAudioFrameViewSink sink)
        {
            EncodeCount++;
            ReadOnlySpan<byte> payload = [0x11, 0x22, 0x33];
            return sink.TryWrite(new EncodedAudioFrameView(OutputFormat, payload, frame.Duration))
                ? AudioCodecStatus.Success
                : AudioCodecStatus.SinkBackpressure;
        }
    }

    private sealed class CapturingProtectedPacketSink : IWebRtcProtectedPacketSink
    {
        private readonly byte[] lastPacket = new byte[128];

        public int PacketCount { get; private set; }

        public ReadOnlySpan<byte> LastPacket => lastPacket.AsSpan(0, LastPacketLength);

        public int LastPacketLength { get; private set; }

        public bool TryWrite(ReadOnlySpan<byte> packet)
        {
            PacketCount++;
            LastPacketLength = packet.Length;
            packet.CopyTo(lastPacket);
            return true;
        }
    }

    private sealed class NoOpPacketProtector : IPacketProtector
    {
        public int MaximumExpansionBytes => 0;

        public int ProtectCount { get; private set; }

        public PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
        {
            ProtectCount++;
            outputLength = inputLength;
            return PacketProtectionStatus.Success;
        }

        public PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = inputLength;
            return PacketProtectionStatus.Success;
        }
    }

    private sealed class ExpandingPacketProtector(int expansionBytes) : IPacketProtector
    {
        public int MaximumExpansionBytes => expansionBytes;

        public PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = 0;
            if (packet.Length < inputLength + expansionBytes)
            {
                return PacketProtectionStatus.DestinationTooSmall;
            }

            packet.Slice(inputLength, expansionBytes).Fill(0xAA);
            outputLength = inputLength + expansionBytes;
            return PacketProtectionStatus.Success;
        }

        public PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = inputLength - expansionBytes;
            return PacketProtectionStatus.Success;
        }
    }
}
