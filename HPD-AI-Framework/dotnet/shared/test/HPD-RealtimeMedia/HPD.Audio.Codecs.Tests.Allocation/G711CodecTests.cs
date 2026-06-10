#nullable enable

using System.Buffers.Binary;
using HPD.Events.Struct;
using HPD.Audio.Codecs.G711;
using HPD.Audio.Primitives;
using HPD.Media.Diagnostics;

namespace HPD.Audio.Codecs.Tests.Allocation;

public sealed class G711CodecTests
{
    private static readonly AudioFormat PcmFormat = new()
    {
        SampleRate = 8000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16
    };

    private static readonly EncodedAudioFormat PcmuFormat = new()
    {
        Encoding = AudioEncoding.Pcmu,
        SampleRate = 8000,
        ChannelCount = 1,
        RtpClockRate = 8000
    };

    private static readonly EncodedAudioFormat PcmaFormat = new()
    {
        Encoding = AudioEncoding.Pcma,
        SampleRate = 8000,
        ChannelCount = 1,
        RtpClockRate = 8000
    };

    [Fact]
    public void DecodeSample_DecodesKnownZeroCodes()
    {
        Assert.Equal(0, G711Codec.DecodeSample(0xFF, AudioEncoding.Pcmu));
        Assert.Equal(0, G711Codec.DecodeSample(0x7F, AudioEncoding.Pcmu));
        Assert.Equal(8, G711Codec.DecodeSample(0xD5, AudioEncoding.Pcma));
        Assert.Equal(-8, G711Codec.DecodeSample(0x55, AudioEncoding.Pcma));
    }

    [Fact]
    public void EncodeSample_EncodesKnownNearZeroCodes()
    {
        Assert.Equal(0xFF, G711Codec.EncodeSample(0, AudioEncoding.Pcmu));
        Assert.Equal(0xD5, G711Codec.EncodeSample(0, AudioEncoding.Pcma));
    }

    [Theory]
    [InlineData(AudioEncoding.Pcmu)]
    [InlineData(AudioEncoding.Pcma)]
    public void EncodeDecode_RoundTripsWithinCompandingTolerance(AudioEncoding encoding)
    {
        short[] samples = [-30000, -12000, -1000, 0, 1000, 12000, 30000];
        Span<byte> encoded = stackalloc byte[samples.Length];
        Span<byte> pcm = stackalloc byte[samples.Length * 2];
        Span<byte> decoded = stackalloc byte[samples.Length * 2];

        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.Slice(i * 2, 2), samples[i]);
        }

        Assert.Equal(AudioCodecStatus.Success, G711Codec.Encode(pcm, encoding, encoded, out int encodedBytes));
        Assert.Equal(samples.Length, encodedBytes);
        Assert.Equal(AudioCodecStatus.Success, G711Codec.Decode(encoded, encoding, decoded, out int decodedBytes));
        Assert.Equal(samples.Length * 2, decodedBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            short actual = BinaryPrimitives.ReadInt16LittleEndian(decoded.Slice(i * 2, 2));
            Assert.InRange(Math.Abs(actual - samples[i]), 0, 1400);
        }
    }

    [Fact]
    public void Factory_CreatesPcmuCodecPair()
    {
        var factory = new G711CodecFactory();

        Assert.True(factory.TryCreateDecoder(PcmuFormat, out IAudioDecoder decoder));
        Assert.True(factory.TryCreateEncoder(PcmFormat, PcmuFormat, out IAudioEncoder encoder));
        Assert.IsType<G711Decoder>(decoder);
        Assert.IsType<G711Encoder>(encoder);
    }

    [Fact]
    public void Factory_RejectsUnusableFormats()
    {
        var factory = new G711CodecFactory();

        bool zeroRateDecoder = factory.TryCreateDecoder(PcmuFormat with { SampleRate = 0 }, out _);
        bool zeroClockDecoder = factory.TryCreateDecoder(PcmuFormat with { RtpClockRate = 0 }, out _);
        bool zeroChannelEncoder = factory.TryCreateEncoder(PcmFormat with { ChannelCount = 0 }, PcmuFormat, out _);
        bool mismatchedOutput = factory.TryCreateEncoder(PcmFormat, PcmuFormat with { SampleRate = 16000 }, out _);

        Assert.False(zeroRateDecoder);
        Assert.False(zeroClockDecoder);
        Assert.False(zeroChannelEncoder);
        Assert.False(mismatchedOutput);
    }

    [Fact]
    public void Constructors_RejectUnusableFormats()
    {
        Assert.Throws<ArgumentException>(() => new G711Decoder(PcmuFormat with { ChannelCount = 0 }));
        Assert.Throws<ArgumentException>(() => new G711Decoder(PcmuFormat with { RtpClockRate = 0 }));
        Assert.Throws<ArgumentException>(() => new G711Encoder(PcmFormat with { SampleRate = 0 }, PcmuFormat));
        Assert.Throws<ArgumentException>(() => new G711Encoder(PcmFormat, PcmuFormat with { ChannelCount = 2 }));
        Assert.Throws<ArgumentException>(() => new G711Encoder(PcmFormat, PcmuFormat, maxPcmBytes: 319));
    }

    [Fact]
    public void CodecFunctions_ReturnStatusForDestinationPressureAndInvalidInput()
    {
        Span<byte> encodedDestination = stackalloc byte[1];
        Span<byte> decodedDestination = stackalloc byte[1];

        AudioCodecStatus encodeStatus = G711Codec.Encode([0x01, 0x00, 0x02, 0x00], AudioEncoding.Pcmu, encodedDestination, out int encodedBytes);
        AudioCodecStatus oddPcmStatus = G711Codec.Encode([0x01], AudioEncoding.Pcmu, encodedDestination, out int oddPcmBytes);
        AudioCodecStatus decodeStatus = G711Codec.Decode([0xFF], AudioEncoding.Pcmu, decodedDestination, out int decodedBytes);

        Assert.Equal(AudioCodecStatus.DestinationTooSmall, encodeStatus);
        Assert.Equal(0, encodedBytes);
        Assert.Equal(AudioCodecStatus.InvalidInput, oddPcmStatus);
        Assert.Equal(0, oddPcmBytes);
        Assert.Equal(AudioCodecStatus.DestinationTooSmall, decodeStatus);
        Assert.Equal(0, decodedBytes);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsSinkBackpressure()
    {
        var encoder = new G711Encoder(PcmFormat, PcmuFormat);
        byte[] pcm = new byte[320];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);
        var sink = new RejectingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.SinkBackpressure, status);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsUnsupportedFormatForMismatchedPcmFormat()
    {
        var encoder = new G711Encoder(PcmFormat, PcmuFormat);
        byte[] pcm = new byte[640];
        var frame = new AudioFrameView(
            pcm,
            PcmFormat with { ChannelCount = 2 },
            160);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.UnsupportedFormat, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsInvalidInputForMalformedPcmFrameSize()
    {
        var encoder = new G711Encoder(PcmFormat, PcmuFormat);
        byte[] pcm = new byte[318];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.InvalidInput, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsInvalidInputForEmptyFrame()
    {
        var encoder = new G711Encoder(PcmFormat, PcmuFormat);
        var frame = new AudioFrameView([], PcmFormat, 0);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.InvalidInput, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeDecoder_ReturnsSinkBackpressure()
    {
        var decoder = new G711Decoder(PcmuFormat);
        byte[] payload = new byte[160];
        var input = new AudioDecodeInputView(PcmuFormat, TimeSpan.FromMilliseconds(20), DecodeMode.Primary, payload);
        var sink = new RejectingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.SinkBackpressure, status);
    }

    [Fact]
    public void RealtimeDecoder_ReturnsUnsupportedFormatForMismatchedEncodedFormat()
    {
        var decoder = new G711Decoder(PcmuFormat);
        byte[] payload = new byte[160];
        var input = new AudioDecodeInputView(
            PcmuFormat with { ChannelCount = 2 },
            TimeSpan.FromMilliseconds(20),
            DecodeMode.Primary,
            payload);
        var sink = new CountingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.UnsupportedFormat, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void Decoder_ReturnsInvalidInputForUnknownDecodeMode()
    {
        var decoder = new G711Decoder(PcmuFormat);
        byte[] payload = new byte[160];
        var retainedInput = new AudioDecodeInput
        {
            Format = PcmuFormat,
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = (DecodeMode)99,
            Payload = payload
        };
        var realtimeInput = new AudioDecodeInputView(
            PcmuFormat,
            TimeSpan.FromMilliseconds(20),
            (DecodeMode)99,
            payload);
        var retainedSink = new CountingAudioFrameSink();
        var realtimeSink = new CountingAudioFrameViewSink();

        AudioCodecStatus retainedStatus = decoder.Decode(retainedInput, retainedSink);
        AudioCodecStatus realtimeStatus = decoder.Decode(realtimeInput, realtimeSink);

        Assert.Equal(AudioCodecStatus.InvalidInput, retainedStatus);
        Assert.Equal(AudioCodecStatus.InvalidInput, realtimeStatus);
        Assert.Equal(0, retainedSink.Count);
        Assert.Equal(0, realtimeSink.Count);
    }

    [Fact]
    public void Decoder_ReturnsInvalidInputForUnsupportedFecRecoveryMode()
    {
        var decoder = new G711Decoder(PcmuFormat);
        byte[] payload = new byte[160];
        var retainedInput = new AudioDecodeInput
        {
            Format = PcmuFormat,
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = DecodeMode.RecoverPreviousFromFec,
            Payload = payload
        };
        var realtimeInput = new AudioDecodeInputView(
            PcmuFormat,
            TimeSpan.FromMilliseconds(20),
            DecodeMode.RecoverPreviousFromFec,
            payload);
        var retainedSink = new CountingAudioFrameSink();
        var realtimeSink = new CountingAudioFrameViewSink();

        AudioCodecStatus retainedStatus = decoder.Decode(retainedInput, retainedSink);
        AudioCodecStatus realtimeStatus = decoder.Decode(realtimeInput, realtimeSink);

        Assert.Equal(AudioCodecStatus.InvalidInput, retainedStatus);
        Assert.Equal(AudioCodecStatus.InvalidInput, realtimeStatus);
        Assert.Equal(0, retainedSink.Count);
        Assert.Equal(0, realtimeSink.Count);
    }

    [Fact]
    public void Decoder_ReturnsUnsupportedFormatForUnusableDecodeInputClockRate()
    {
        var decoder = new G711Decoder(PcmuFormat);
        byte[] payload = new byte[160];
        var retainedInput = new AudioDecodeInput
        {
            Format = PcmuFormat with { RtpClockRate = 0 },
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = DecodeMode.Primary,
            Payload = payload
        };
        var realtimeInput = new AudioDecodeInputView(
            PcmuFormat with { RtpClockRate = 0 },
            TimeSpan.FromMilliseconds(20),
            DecodeMode.Primary,
            payload);
        var retainedSink = new CountingAudioFrameSink();
        var realtimeSink = new CountingAudioFrameViewSink();

        AudioCodecStatus retainedStatus = decoder.Decode(retainedInput, retainedSink);
        AudioCodecStatus realtimeStatus = decoder.Decode(realtimeInput, realtimeSink);

        Assert.Equal(AudioCodecStatus.UnsupportedFormat, retainedStatus);
        Assert.Equal(AudioCodecStatus.UnsupportedFormat, realtimeStatus);
        Assert.Equal(0, retainedSink.Count);
        Assert.Equal(0, realtimeSink.Count);
    }

    [Fact]
    public void Decoder_ReturnsUnsupportedFormatForMismatchedDecodeInputClockRate()
    {
        var decoder = new G711Decoder(PcmuFormat);
        byte[] payload = new byte[160];
        var input = new AudioDecodeInputView(
            PcmuFormat with { RtpClockRate = 16000 },
            TimeSpan.FromMilliseconds(20),
            DecodeMode.Primary,
            payload);
        var sink = new CountingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.UnsupportedFormat, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeDecoder_ReturnsSuccessWithoutFrameForEmptyPayload()
    {
        var decoder = new G711Decoder(PcmuFormat);
        var input = new AudioDecodeInputView(
            PcmuFormat,
            TimeSpan.Zero,
            DecodeMode.Primary,
            []);
        var sink = new CountingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.Success, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncodeDecode_DoesNotAllocateAfterWarmup()
    {
        var encoder = new G711Encoder(PcmFormat, PcmuFormat);
        var decoder = new G711Decoder(PcmuFormat);
        var audioSink = new CountingAudioFrameViewSink();
        var encodedSink = new DecodingEncodedViewSink(decoder, audioSink);
        byte[] pcm = new byte[320];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), (short)(Math.Sin(i / 10.0) * 12000));
        }

        var frame = new AudioFrameView(pcm, PcmFormat, 160);

        for (int i = 0; i < 1_000; i++)
        {
            encodedSink.Reset();
            if (encoder.Encode(frame, encodedSink) != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("G.711 encode failed during warmup.");
            }

            if (encodedSink.LastDecodeStatus != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("G.711 decode failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            encodedSink.Reset();
            if (encoder.Encode(frame, encodedSink) != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("G.711 encode failed during allocation measurement.");
            }

            if (encodedSink.LastDecodeStatus != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("G.711 decode failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void RealtimeEncodeDecode_EmitsCodecTimingTelemetry()
    {
        using var hub = new StructEventHub();
        using StructEventInbox<CodecTimingSample> inbox = hub
            .Route<CodecTimingSample>(RealtimeMediaTelemetry.RouteOptions)
            .CreateInbox(new StructEventInboxOptions { Capacity = 4 });
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var encoder = new G711Encoder(PcmFormat, PcmuFormat, emitters);
        var decoder = new G711Decoder(PcmuFormat, emitters);
        var audioSink = new CountingAudioFrameViewSink();
        var encodedSink = new DecodingEncodedViewSink(decoder, audioSink);
        byte[] pcm = new byte[320];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);

        AudioCodecStatus status = encoder.Encode(frame, encodedSink);

        Assert.Equal(AudioCodecStatus.Success, status);
        Assert.True(inbox.TryRead(out CodecTimingSample decode));
        Assert.True(inbox.TryRead(out CodecTimingSample encode));
        Assert.Equal(CodecOperation.Decode, decode.Operation);
        Assert.Equal((int)AudioEncoding.Pcmu, decode.Encoding);
        Assert.Equal(160, decode.InputBytes);
        Assert.Equal(320, decode.OutputBytes);
        Assert.Equal(CodecOperation.Encode, encode.Operation);
        Assert.Equal((int)AudioEncoding.Pcmu, encode.Encoding);
        Assert.Equal(320, encode.InputBytes);
        Assert.Equal(160, encode.OutputBytes);
    }

    [Fact]
    public void RealtimeEncodeDecode_TelemetryWithNoSubscribersDoesNotAllocateAfterWarmup()
    {
        using var hub = new StructEventHub();
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var encoder = new G711Encoder(PcmFormat, PcmuFormat, emitters);
        var decoder = new G711Decoder(PcmuFormat, emitters);
        var audioSink = new CountingAudioFrameViewSink();
        var encodedSink = new DecodingEncodedViewSink(decoder, audioSink);
        byte[] pcm = new byte[320];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);

        for (int i = 0; i < 1_000; i++)
        {
            encodedSink.Reset();
            if (encoder.Encode(frame, encodedSink) != AudioCodecStatus.Success ||
                encodedSink.LastDecodeStatus != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("G.711 telemetry encode/decode failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            encodedSink.Reset();
            if (encoder.Encode(frame, encodedSink) != AudioCodecStatus.Success ||
                encodedSink.LastDecodeStatus != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("G.711 telemetry encode/decode failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    private sealed class DecodingEncodedViewSink(G711Decoder decoder, IAudioFrameViewSink audioSink) : IEncodedAudioFrameViewSink
    {
        public AudioCodecStatus LastDecodeStatus { get; private set; }

        public bool TryWrite(in EncodedAudioFrameView frame)
        {
            var input = new AudioDecodeInputView(frame.Format, frame.Duration, DecodeMode.Primary, frame.Data);
            LastDecodeStatus = decoder.Decode(input, audioSink);
            return LastDecodeStatus == AudioCodecStatus.Success;
        }

        public void Reset() => LastDecodeStatus = default;
    }

    private sealed class CountingAudioFrameViewSink : IAudioFrameViewSink
    {
        public int Count { get; private set; }

        public bool TryWrite(in AudioFrameView frame)
        {
            if (frame.Data.Length != 320)
            {
                return false;
            }

            Count++;
            return true;
        }
    }

    private sealed class CountingAudioFrameSink : IAudioFrameSink
    {
        public int Count { get; private set; }

        public bool TryWrite(in AudioFrame frame)
        {
            Count++;
            return true;
        }
    }

    private sealed class CountingEncodedAudioFrameViewSink : IEncodedAudioFrameViewSink
    {
        public int Count { get; private set; }

        public bool TryWrite(in EncodedAudioFrameView frame)
        {
            Count++;
            return true;
        }
    }

    private sealed class RejectingEncodedAudioFrameViewSink : IEncodedAudioFrameViewSink
    {
        public bool TryWrite(in EncodedAudioFrameView frame) => false;
    }

    private sealed class RejectingAudioFrameViewSink : IAudioFrameViewSink
    {
        public bool TryWrite(in AudioFrameView frame) => false;
    }
}
