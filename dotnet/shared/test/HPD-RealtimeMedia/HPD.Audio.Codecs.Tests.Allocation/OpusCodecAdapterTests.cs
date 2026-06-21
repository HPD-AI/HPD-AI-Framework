#nullable enable

using HPD.Audio.Codecs.Opus;
using HPD.Audio.Primitives;

namespace HPD.Audio.Codecs.Tests.Allocation;

public sealed class OpusCodecAdapterTests
{
    private static readonly AudioFormat PcmFormat = new()
    {
        SampleRate = 48000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16
    };

    private static readonly EncodedAudioFormat OpusFormat = new()
    {
        Encoding = AudioEncoding.Opus,
        SampleRate = 48000,
        ChannelCount = 1,
        RtpClockRate = 48000
    };

    [Fact]
    public void Factory_CreatesExplicitProviderBackedOpusCodecPair()
    {
        var providerFactory = new PassthroughOpusProviderFactory();
        var factory = new OpusCodecFactory(providerFactory);

        Assert.True(factory.TryCreateDecoder(OpusFormat, out IAudioDecoder decoder));
        Assert.True(factory.TryCreateEncoder(PcmFormat, OpusFormat, out IAudioEncoder encoder));
        Assert.IsType<OpusDecoder>(decoder);
        Assert.IsType<OpusEncoder>(encoder);
    }

    [Fact]
    public void Factory_RejectsUnusableFormatsBeforeProviderConstruction()
    {
        var providerFactory = new PassthroughOpusProviderFactory();
        var factory = new OpusCodecFactory(providerFactory);

        bool zeroRateDecoder = factory.TryCreateDecoder(OpusFormat with { SampleRate = 0 }, out _);
        bool zeroClockDecoder = factory.TryCreateDecoder(OpusFormat with { RtpClockRate = 0 }, out _);
        bool zeroChannelEncoder = factory.TryCreateEncoder(PcmFormat with { ChannelCount = 0 }, OpusFormat, out _);
        bool mismatchedOutput = factory.TryCreateEncoder(PcmFormat, OpusFormat with { SampleRate = 16000 }, out _);
        bool mismatchedClock = factory.TryCreateEncoder(PcmFormat, OpusFormat with { RtpClockRate = 16000 }, out _);

        Assert.False(zeroRateDecoder);
        Assert.False(zeroClockDecoder);
        Assert.False(zeroChannelEncoder);
        Assert.False(mismatchedOutput);
        Assert.False(mismatchedClock);
    }

    [Fact]
    public void Constructors_RejectUnusableProviderFormats()
    {
        Assert.Throws<ArgumentException>(
            () => new OpusDecoder(OpusFormat with { ChannelCount = 0 }, new PassthroughOpusDecoderProvider(PcmFormat)));
        Assert.Throws<ArgumentException>(
            () => new OpusDecoder(OpusFormat, new PassthroughOpusDecoderProvider(PcmFormat with { ChannelCount = 0 })));
        Assert.Throws<ArgumentException>(
            () => new OpusDecoder(OpusFormat, new PassthroughOpusDecoderProvider(PcmFormat with { SampleRate = 16000 })));
        Assert.Throws<ArgumentException>(
            () => new OpusDecoder(OpusFormat with { RtpClockRate = 16000 }, new PassthroughOpusDecoderProvider(PcmFormat)));
        Assert.Throws<ArgumentException>(
            () => new OpusEncoder(new PassthroughOpusEncoderProvider(PcmFormat with { SampleRate = 0 }, OpusFormat)));
        Assert.Throws<ArgumentException>(
            () => new OpusEncoder(new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat with { RtpClockRate = 0 })));
        Assert.Throws<ArgumentException>(
            () => new OpusEncoder(new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat with { RtpClockRate = 16000 })));
        Assert.Throws<ArgumentException>(
            () => new OpusEncoder(new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat with { ChannelCount = 2 })));
    }

    [Fact]
    public void DecoderConstructor_RejectsPcmScratchSizeThatCannotHoldWholeSampleFrames()
    {
        var stereoPcm = PcmFormat with { ChannelCount = 2 };
        var stereoOpus = OpusFormat with { ChannelCount = 2 };

        Assert.Throws<ArgumentException>(
            () => new OpusDecoder(
                stereoOpus,
                new PassthroughOpusDecoderProvider(stereoPcm),
                new OpusCodecOptions { MaxPcmBytes = 514 }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(513)]
    [InlineData(319)]
    public void RealtimeDecoder_ReturnsInvalidInputForMalformedProviderByteCount(int bytesWritten)
    {
        var decoder = new OpusDecoder(
            OpusFormat,
            new MalformedOpusDecoderProvider(PcmFormat, bytesWritten),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        var input = new AudioDecodeInputView(
            OpusFormat,
            TimeSpan.FromMilliseconds(20),
            DecodeMode.Primary,
            new byte[320]);
        var sink = new CountingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.InvalidInput, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeDecoder_ReturnsSuccessWithoutFrameWhenProviderWritesZeroBytes()
    {
        var decoder = new OpusDecoder(
            OpusFormat,
            new MalformedOpusDecoderProvider(PcmFormat, 0),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        var input = new AudioDecodeInputView(
            OpusFormat,
            TimeSpan.FromMilliseconds(20),
            DecodeMode.Primary,
            new byte[320]);
        var sink = new CountingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.Success, status);
        Assert.Equal(0, sink.Count);
    }

    [Theory]
    [InlineData(DecodeMode.Primary, 0)]
    [InlineData(DecodeMode.Primary, 17)]
    [InlineData(DecodeMode.ConcealLoss, 17)]
    [InlineData(DecodeMode.RecoverPreviousFromFec, 17)]
    public void RealtimeDecoder_ReturnsInvalidInputForInvalidPayloadSize(DecodeMode mode, int payloadLength)
    {
        var decoder = new OpusDecoder(
            OpusFormat,
            new PassthroughOpusDecoderProvider(PcmFormat),
            new OpusCodecOptions { MaxEncodedBytes = 16, MaxPcmBytes = 512 });
        byte[] payload = new byte[payloadLength];
        var input = new AudioDecodeInputView(
            OpusFormat,
            TimeSpan.FromMilliseconds(20),
            mode,
            payload,
            inBandFecNegotiated: mode == DecodeMode.RecoverPreviousFromFec);
        var sink = new CountingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.InvalidInput, status);
        Assert.Equal(0, sink.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(513)]
    public void RealtimeEncoder_ReturnsInvalidInputForMalformedProviderByteCount(int bytesWritten)
    {
        var encoder = new OpusEncoder(
            new MalformedOpusEncoderProvider(PcmFormat, OpusFormat, bytesWritten),
            new OpusCodecOptions { MaxEncodedBytes = 512 });
        byte[] pcm = new byte[320];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.InvalidInput, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsSuccessWithoutAccessUnitWhenProviderWritesZeroBytes()
    {
        var encoder = new OpusEncoder(
            new MalformedOpusEncoderProvider(PcmFormat, OpusFormat, 0),
            new OpusCodecOptions { MaxEncodedBytes = 512 });
        byte[] pcm = new byte[320];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.Success, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsInvalidInputForEmptyFrame()
    {
        var encoder = new OpusEncoder(
            new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat),
            new OpusCodecOptions { MaxEncodedBytes = 512 });
        var frame = new AudioFrameView([], PcmFormat, 0);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.InvalidInput, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncodeDecode_DoesNotAllocateAfterWarmup()
    {
        var providerFactory = new PassthroughOpusProviderFactory();
        var factory = new OpusCodecFactory(providerFactory, new OpusCodecOptions
        {
            MaxEncodedBytes = 512,
            MaxPcmBytes = 512
        });
        Assert.True(factory.TryCreateDecoder(OpusFormat, out IAudioDecoder retainedDecoder));
        Assert.True(factory.TryCreateEncoder(PcmFormat, OpusFormat, out IAudioEncoder retainedEncoder));
        var decoder = Assert.IsAssignableFrom<IRealtimeAudioDecoder>(retainedDecoder);
        var encoder = Assert.IsAssignableFrom<IRealtimeAudioEncoder>(retainedEncoder);
        var audioSink = new CountingAudioFrameViewSink();
        var encodedSink = new DecodingEncodedViewSink(decoder, audioSink);
        byte[] pcm = new byte[320];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (byte)i;
        }

        var frame = new AudioFrameView(pcm, PcmFormat, 160);

        for (int i = 0; i < 1_000; i++)
        {
            encodedSink.Reset();
            if (encoder.Encode(frame, encodedSink) != AudioCodecStatus.Success ||
                encodedSink.LastDecodeStatus != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("Opus adapter encode/decode failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            encodedSink.Reset();
            if (encoder.Encode(frame, encodedSink) != AudioCodecStatus.Success ||
                encodedSink.LastDecodeStatus != AudioCodecStatus.Success)
            {
                throw new InvalidOperationException("Opus adapter encode/decode failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void DecodeConcealLoss_MarksFrameAsPacketLossConcealment()
    {
        var decoder = new OpusDecoder(OpusFormat, new PassthroughOpusDecoderProvider(PcmFormat), new OpusCodecOptions());
        var sink = new CapturingAudioFrameSink();
        var input = new AudioDecodeInput
        {
            Format = OpusFormat,
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = DecodeMode.ConcealLoss,
            Payload = new byte[16]
        };

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.Success, status);
        Assert.Equal(AudioRecoveryKind.PacketLossConcealment, sink.Frame.RecoveryKind);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsProviderDestinationTooSmall()
    {
        var encoder = new OpusEncoder(
            new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat),
            new OpusCodecOptions { MaxEncodedBytes = 4 });
        byte[] pcm = new byte[320];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.DestinationTooSmall, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsUnsupportedFormatForMismatchedPcmFormat()
    {
        var encoder = new OpusEncoder(
            new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat),
            new OpusCodecOptions { MaxEncodedBytes = 512 });
        byte[] pcm = new byte[640];
        var frame = new AudioFrameView(pcm, PcmFormat with { ChannelCount = 2 }, 160);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.UnsupportedFormat, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsInvalidInputForMalformedPcmFrameSize()
    {
        var encoder = new OpusEncoder(
            new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat),
            new OpusCodecOptions { MaxEncodedBytes = 512 });
        byte[] pcm = new byte[318];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);
        var sink = new CountingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.InvalidInput, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void RealtimeEncoder_ReturnsSinkBackpressure()
    {
        var encoder = new OpusEncoder(
            new PassthroughOpusEncoderProvider(PcmFormat, OpusFormat),
            new OpusCodecOptions { MaxEncodedBytes = 512 });
        byte[] pcm = new byte[320];
        var frame = new AudioFrameView(pcm, PcmFormat, 160);
        var sink = new RejectingEncodedAudioFrameViewSink();

        AudioCodecStatus status = encoder.Encode(frame, sink);

        Assert.Equal(AudioCodecStatus.SinkBackpressure, status);
    }

    [Fact]
    public void RealtimeDecoder_ReturnsSinkBackpressure()
    {
        var decoder = new OpusDecoder(
            OpusFormat,
            new PassthroughOpusDecoderProvider(PcmFormat),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        byte[] payload = new byte[320];
        var input = new AudioDecodeInputView(OpusFormat, TimeSpan.FromMilliseconds(20), DecodeMode.Primary, payload);
        var sink = new RejectingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.SinkBackpressure, status);
    }

    [Fact]
    public void RealtimeDecoder_ReturnsUnsupportedFormatForMismatchedEncodedFormat()
    {
        var decoder = new OpusDecoder(
            OpusFormat,
            new PassthroughOpusDecoderProvider(PcmFormat),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        byte[] payload = new byte[320];
        var input = new AudioDecodeInputView(
            OpusFormat with { ChannelCount = 2 },
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
        var decoder = new OpusDecoder(
            OpusFormat,
            new PassthroughOpusDecoderProvider(PcmFormat),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        byte[] payload = new byte[320];
        var retainedInput = new AudioDecodeInput
        {
            Format = OpusFormat,
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = (DecodeMode)99,
            Payload = payload
        };
        var realtimeInput = new AudioDecodeInputView(
            OpusFormat,
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
    public void Decoder_ReturnsUnsupportedFormatForUnusableDecodeInputClockRate()
    {
        var decoder = new OpusDecoder(
            OpusFormat,
            new PassthroughOpusDecoderProvider(PcmFormat),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        byte[] payload = new byte[320];
        var retainedInput = new AudioDecodeInput
        {
            Format = OpusFormat with { RtpClockRate = 0 },
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = DecodeMode.Primary,
            Payload = payload
        };
        var realtimeInput = new AudioDecodeInputView(
            OpusFormat with { RtpClockRate = 0 },
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
        var decoder = new OpusDecoder(
            OpusFormat,
            new PassthroughOpusDecoderProvider(PcmFormat),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        byte[] payload = new byte[320];
        var input = new AudioDecodeInputView(
            OpusFormat with { RtpClockRate = 16000 },
            TimeSpan.FromMilliseconds(20),
            DecodeMode.Primary,
            payload);
        var sink = new CountingAudioFrameViewSink();

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.UnsupportedFormat, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void DecodeRecoverPreviousFromFec_MarksFrameAsForwardErrorCorrection()
    {
        var decoder = new OpusDecoder(OpusFormat, new PassthroughOpusDecoderProvider(PcmFormat), new OpusCodecOptions());
        var sink = new CapturingAudioFrameSink();
        var input = new AudioDecodeInput
        {
            Format = OpusFormat,
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = DecodeMode.RecoverPreviousFromFec,
            Payload = new byte[16],
            InBandFecNegotiated = true
        };

        AudioCodecStatus status = decoder.Decode(input, sink);

        Assert.Equal(AudioCodecStatus.Success, status);
        Assert.Equal(AudioRecoveryKind.ForwardErrorCorrection, sink.Frame.RecoveryKind);
    }

    [Theory]
    [InlineData(false, 16)]
    [InlineData(true, 0)]
    public void Decoder_ReturnsInvalidInputForInvalidFecRecoveryOperation(bool inBandFecNegotiated, int payloadLength)
    {
        var decoder = new OpusDecoder(
            OpusFormat,
            new PassthroughOpusDecoderProvider(PcmFormat),
            new OpusCodecOptions { MaxPcmBytes = 512 });
        byte[] payload = new byte[payloadLength];
        var retainedInput = new AudioDecodeInput
        {
            Format = OpusFormat,
            Duration = TimeSpan.FromMilliseconds(20),
            Mode = DecodeMode.RecoverPreviousFromFec,
            Payload = payload,
            InBandFecNegotiated = inBandFecNegotiated
        };
        var realtimeInput = new AudioDecodeInputView(
            OpusFormat,
            TimeSpan.FromMilliseconds(20),
            DecodeMode.RecoverPreviousFromFec,
            payload,
            inBandFecNegotiated: inBandFecNegotiated);
        var retainedSink = new CountingAudioFrameSink();
        var realtimeSink = new CountingAudioFrameViewSink();

        AudioCodecStatus retainedStatus = decoder.Decode(retainedInput, retainedSink);
        AudioCodecStatus realtimeStatus = decoder.Decode(realtimeInput, realtimeSink);

        Assert.Equal(AudioCodecStatus.InvalidInput, retainedStatus);
        Assert.Equal(AudioCodecStatus.InvalidInput, realtimeStatus);
        Assert.Equal(0, retainedSink.Count);
        Assert.Equal(0, realtimeSink.Count);
    }

    private sealed class PassthroughOpusProviderFactory : IOpusCodecProviderFactory
    {
        public bool TryCreateDecoderProvider(
            in EncodedAudioFormat format,
            OpusCodecOptions options,
            out IOpusDecoderProvider provider)
        {
            provider = null!;
            if (format.Encoding != AudioEncoding.Opus)
            {
                return false;
            }

            provider = new PassthroughOpusDecoderProvider(new AudioFormat
            {
                SampleRate = format.SampleRate,
                ChannelCount = format.ChannelCount,
                SampleFormat = AudioSampleFormat.Pcm16
            });
            return true;
        }

        public bool TryCreateEncoderProvider(
            in AudioFormat inputFormat,
            in EncodedAudioFormat outputFormat,
            OpusCodecOptions options,
            out IOpusEncoderProvider provider)
        {
            provider = null!;
            if (outputFormat.Encoding != AudioEncoding.Opus)
            {
                return false;
            }

            provider = new PassthroughOpusEncoderProvider(inputFormat, outputFormat);
            return true;
        }
    }

    private sealed class PassthroughOpusDecoderProvider(AudioFormat outputFormat) : IOpusDecoderProvider
    {
        public AudioFormat OutputFormat { get; } = outputFormat;

        public AudioCodecStatus Decode(
            ReadOnlySpan<byte> payload,
            DecodeMode mode,
            TimeSpan duration,
            bool inBandFecNegotiated,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = Math.Min(payload.Length, destination.Length);
            payload[..bytesWritten].CopyTo(destination);
            return AudioCodecStatus.Success;
        }

        public void Dispose()
        {
        }
    }

    private sealed class MalformedOpusDecoderProvider(AudioFormat outputFormat, int bytesWrittenResult) : IOpusDecoderProvider
    {
        public AudioFormat OutputFormat { get; } = outputFormat;

        public AudioCodecStatus Decode(
            ReadOnlySpan<byte> payload,
            DecodeMode mode,
            TimeSpan duration,
            bool inBandFecNegotiated,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = bytesWrittenResult;
            return AudioCodecStatus.Success;
        }

        public void Dispose()
        {
        }
    }

    private sealed class PassthroughOpusEncoderProvider(AudioFormat inputFormat, EncodedAudioFormat outputFormat) : IOpusEncoderProvider
    {
        public AudioFormat InputFormat { get; } = inputFormat;

        public EncodedAudioFormat OutputFormat { get; } = outputFormat;

        public AudioCodecStatus Encode(ReadOnlySpan<byte> pcm, int samplesPerChannel, Span<byte> destination, out int bytesWritten)
        {
            if (pcm.Length > destination.Length)
            {
                bytesWritten = 0;
                return AudioCodecStatus.DestinationTooSmall;
            }

            pcm.CopyTo(destination);
            bytesWritten = pcm.Length;
            return AudioCodecStatus.Success;
        }

        public void Dispose()
        {
        }
    }

    private sealed class MalformedOpusEncoderProvider(
        AudioFormat inputFormat,
        EncodedAudioFormat outputFormat,
        int bytesWrittenResult) : IOpusEncoderProvider
    {
        public AudioFormat InputFormat { get; } = inputFormat;

        public EncodedAudioFormat OutputFormat { get; } = outputFormat;

        public AudioCodecStatus Encode(ReadOnlySpan<byte> pcm, int samplesPerChannel, Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = bytesWrittenResult;
            return AudioCodecStatus.Success;
        }

        public void Dispose()
        {
        }
    }

    private sealed class DecodingEncodedViewSink(IRealtimeAudioDecoder decoder, IAudioFrameViewSink audioSink) : IEncodedAudioFrameViewSink
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

    private sealed class CapturingAudioFrameSink : IAudioFrameSink
    {
        public AudioFrame Frame { get; private set; }

        public bool TryWrite(in AudioFrame frame)
        {
            Frame = frame;
            return true;
        }
    }
}
