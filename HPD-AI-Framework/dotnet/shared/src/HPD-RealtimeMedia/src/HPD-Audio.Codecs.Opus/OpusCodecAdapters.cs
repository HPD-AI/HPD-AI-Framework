#nullable enable

using HPD.Audio.Primitives;

namespace HPD.Audio.Codecs.Opus;

/// <summary>
/// Configures provider-backed Opus codec adapters.
/// </summary>
public sealed class OpusCodecOptions
{
    /// <summary>Gets the maximum encoded Opus packet size accepted by the decoder.</summary>
    public int MaxEncodedBytes { get; init; } = 4096;

    /// <summary>Gets the maximum PCM bytes produced by the decoder.</summary>
    public int MaxPcmBytes { get; init; } = 8192;
}

/// <summary>
/// Decodes Opus packets into caller-provided PCM storage.
/// </summary>
public interface IOpusDecoderProvider : IDisposable
{
    /// <summary>Gets the decoded PCM output format.</summary>
    AudioFormat OutputFormat { get; }

    /// <summary>Decodes one Opus packet or loss-concealment operation into caller-provided PCM storage.</summary>
    AudioCodecStatus Decode(
        ReadOnlySpan<byte> payload,
        DecodeMode mode,
        TimeSpan duration,
        bool inBandFecNegotiated,
        Span<byte> destination,
        out int bytesWritten);
}

/// <summary>
/// Encodes PCM frames into caller-provided Opus packet storage.
/// </summary>
public interface IOpusEncoderProvider : IDisposable
{
    /// <summary>Gets the PCM input format.</summary>
    AudioFormat InputFormat { get; }

    /// <summary>Gets the encoded Opus output format.</summary>
    EncodedAudioFormat OutputFormat { get; }

    /// <summary>Encodes one PCM frame into caller-provided Opus packet storage.</summary>
    AudioCodecStatus Encode(
        ReadOnlySpan<byte> pcm,
        int samplesPerChannel,
        Span<byte> destination,
        out int bytesWritten);
}

/// <summary>
/// Creates Opus providers through explicit typed construction rather than reflection-based activation.
/// </summary>
public interface IOpusCodecProviderFactory
{
    /// <summary>Attempts to create a decoder provider for an Opus format.</summary>
    bool TryCreateDecoderProvider(
        in EncodedAudioFormat format,
        OpusCodecOptions options,
        out IOpusDecoderProvider provider);

    /// <summary>Attempts to create an encoder provider for PCM input and Opus output formats.</summary>
    bool TryCreateEncoderProvider(
        in AudioFormat inputFormat,
        in EncodedAudioFormat outputFormat,
        OpusCodecOptions options,
        out IOpusEncoderProvider provider);
}

/// <summary>
/// Creates Opus codec adapters over an explicitly supplied provider factory.
/// </summary>
public sealed class OpusCodecFactory : IAudioCodecFactory
{
    private readonly IOpusCodecProviderFactory providerFactory;
    private readonly OpusCodecOptions options;

    /// <summary>Initializes a new instance of the <see cref="OpusCodecFactory"/> class.</summary>
    public OpusCodecFactory(IOpusCodecProviderFactory providerFactory, OpusCodecOptions? options = null)
    {
        this.providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        this.options = options ?? new OpusCodecOptions();
    }

    /// <inheritdoc />
    public bool TryCreateDecoder(in EncodedAudioFormat format, out IAudioDecoder decoder)
    {
        decoder = null!;
        if (!OpusFormatValidation.IsUsableEncodedFormat(format) ||
            !providerFactory.TryCreateDecoderProvider(format, options, out IOpusDecoderProvider provider))
        {
            return false;
        }

        decoder = new OpusDecoder(format, provider, options);
        return true;
    }

    /// <inheritdoc />
    public bool TryCreateEncoder(in AudioFormat inputFormat, in EncodedAudioFormat outputFormat, out IAudioEncoder encoder)
    {
        encoder = null!;
        if (!OpusFormatValidation.IsUsablePcmFormat(inputFormat) ||
            !OpusFormatValidation.IsUsableEncodedFormat(outputFormat) ||
            !OpusFormatValidation.Matches(inputFormat, outputFormat) ||
            !providerFactory.TryCreateEncoderProvider(inputFormat, outputFormat, options, out IOpusEncoderProvider provider))
        {
            return false;
        }

        encoder = new OpusEncoder(provider, options);
        return true;
    }
}

/// <summary>
/// Opus decoder adapter backed by an explicit provider.
/// </summary>
public sealed class OpusDecoder : IAudioDecoder, IRealtimeAudioDecoder
{
    private readonly IOpusDecoderProvider provider;
    private readonly byte[] pcmScratch;
    private readonly int maxEncodedBytes;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="OpusDecoder"/> class.</summary>
    public OpusDecoder(EncodedAudioFormat inputFormat, IOpusDecoderProvider provider, OpusCodecOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (inputFormat.Encoding != AudioEncoding.Opus)
        {
            throw new ArgumentException("The input format must be Opus.", nameof(inputFormat));
        }

        OpusCodecOptions resolvedOptions = options ?? new OpusCodecOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolvedOptions.MaxEncodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolvedOptions.MaxPcmBytes);
        if (!OpusFormatValidation.IsUsableEncodedFormat(inputFormat))
        {
            throw new ArgumentException("The input format must declare Opus with a positive sample rate, channel count, and RTP clock rate when present.", nameof(inputFormat));
        }

        if (!OpusFormatValidation.IsUsablePcmFormat(provider.OutputFormat))
        {
            throw new ArgumentException("The decoder provider output format must declare PCM16 with a positive sample rate and channel count.", nameof(provider));
        }

        if (!OpusFormatValidation.Matches(provider.OutputFormat, inputFormat))
        {
            throw new ArgumentException("The decoder provider output format must match the Opus input sample rate and channel count.", nameof(provider));
        }

        InputFormat = inputFormat;
        this.provider = provider;
        OutputFormat = provider.OutputFormat;
        int bytesPerSampleFrame = OutputFormat.ChannelCount * 2;
        if (resolvedOptions.MaxPcmBytes % bytesPerSampleFrame != 0)
        {
            throw new ArgumentException("The maximum PCM byte count must align to whole interleaved PCM16 sample frames.", nameof(options));
        }

        maxEncodedBytes = resolvedOptions.MaxEncodedBytes;
        pcmScratch = new byte[resolvedOptions.MaxPcmBytes];
    }

    /// <summary>Gets the decoder input format.</summary>
    public EncodedAudioFormat InputFormat { get; }

    /// <inheritdoc />
    public AudioFormat OutputFormat { get; }

    /// <inheritdoc />
    public AudioCodecStatus Decode(in AudioDecodeInput input, IAudioFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (disposed)
        {
            return AudioCodecStatus.Disposed;
        }

        if (!IsInputFormatSupported(input.Format))
        {
            return AudioCodecStatus.UnsupportedFormat;
        }

        if (!IsSupportedDecodeMode(input.Mode))
        {
            return AudioCodecStatus.InvalidInput;
        }

        if (!IsValidDecodeOperation(input.Mode, input.InBandFecNegotiated, input.Payload.Length))
        {
            return AudioCodecStatus.InvalidInput;
        }

        AudioCodecStatus status = provider.Decode(
            input.Payload.Span,
            input.Mode,
            input.Duration,
            input.InBandFecNegotiated,
            pcmScratch,
            out int bytesWritten);

        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        status = ValidateDecodedByteCount(bytesWritten);
        if (status != AudioCodecStatus.Success || bytesWritten == 0)
        {
            return status;
        }

        var frame = new AudioFrame
        {
            Data = pcmScratch.AsMemory(0, bytesWritten),
            Format = OutputFormat,
            SamplesPerChannel = bytesWritten / 2 / OutputFormat.ChannelCount,
            RecoveryKind = GetRecoveryKind(input.Mode)
        };

        return sink.TryWrite(frame) ? AudioCodecStatus.Success : AudioCodecStatus.SinkBackpressure;
    }

    /// <inheritdoc />
    public AudioCodecStatus Decode(in AudioDecodeInputView input, IAudioFrameViewSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (disposed)
        {
            return AudioCodecStatus.Disposed;
        }

        if (!IsInputFormatSupported(input.Format))
        {
            return AudioCodecStatus.UnsupportedFormat;
        }

        if (!IsSupportedDecodeMode(input.Mode))
        {
            return AudioCodecStatus.InvalidInput;
        }

        if (!IsValidDecodeOperation(input.Mode, input.InBandFecNegotiated, input.Payload.Length))
        {
            return AudioCodecStatus.InvalidInput;
        }

        AudioCodecStatus status = provider.Decode(
            input.Payload,
            input.Mode,
            input.Duration,
            input.InBandFecNegotiated,
            pcmScratch,
            out int bytesWritten);

        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        status = ValidateDecodedByteCount(bytesWritten);
        if (status != AudioCodecStatus.Success || bytesWritten == 0)
        {
            return status;
        }

        var frame = new AudioFrameView(
            pcmScratch.AsSpan(0, bytesWritten),
            OutputFormat,
            bytesWritten / 2 / OutputFormat.ChannelCount,
            recoveryKind: GetRecoveryKind(input.Mode));

        return sink.TryWrite(frame) ? AudioCodecStatus.Success : AudioCodecStatus.SinkBackpressure;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            provider.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static AudioRecoveryKind GetRecoveryKind(DecodeMode mode)
    {
        return mode switch
        {
            DecodeMode.ConcealLoss => AudioRecoveryKind.PacketLossConcealment,
            DecodeMode.RecoverPreviousFromFec => AudioRecoveryKind.ForwardErrorCorrection,
            _ => AudioRecoveryKind.None
        };
    }

    private static bool IsSupportedDecodeMode(DecodeMode mode)
    {
        return mode is DecodeMode.Primary or DecodeMode.ConcealLoss or DecodeMode.RecoverPreviousFromFec;
    }

    private bool IsValidDecodeOperation(DecodeMode mode, bool inBandFecNegotiated, int payloadLength)
    {
        return mode switch
        {
            DecodeMode.Primary => payloadLength > 0 && payloadLength <= maxEncodedBytes,
            DecodeMode.ConcealLoss => payloadLength == 0 || payloadLength <= maxEncodedBytes,
            DecodeMode.RecoverPreviousFromFec => inBandFecNegotiated && payloadLength > 0 && payloadLength <= maxEncodedBytes,
            _ => false
        };
    }

    private bool IsInputFormatSupported(EncodedAudioFormat format)
    {
        return OpusFormatValidation.IsUsableEncodedFormat(format) &&
            format.Encoding == InputFormat.Encoding &&
            format.SampleRate == InputFormat.SampleRate &&
            format.ChannelCount == InputFormat.ChannelCount &&
            (format.RtpClockRate ?? format.SampleRate) == (InputFormat.RtpClockRate ?? InputFormat.SampleRate);
    }

    private AudioCodecStatus ValidateDecodedByteCount(int bytesWritten)
    {
        int bytesPerSampleFrame = OutputFormat.ChannelCount * 2;
        if (bytesWritten < 0 ||
            bytesWritten > pcmScratch.Length ||
            bytesWritten % bytesPerSampleFrame != 0)
        {
            return AudioCodecStatus.InvalidInput;
        }

        return AudioCodecStatus.Success;
    }
}

/// <summary>
/// Opus encoder adapter backed by an explicit provider.
/// </summary>
public sealed class OpusEncoder : IAudioEncoder, IRealtimeAudioEncoder
{
    private readonly IOpusEncoderProvider provider;
    private readonly byte[] encodedScratch;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="OpusEncoder"/> class.</summary>
    public OpusEncoder(IOpusEncoderProvider provider, OpusCodecOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        OpusCodecOptions resolvedOptions = options ?? new OpusCodecOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolvedOptions.MaxEncodedBytes);
        if (!OpusFormatValidation.IsUsablePcmFormat(provider.InputFormat))
        {
            throw new ArgumentException("The encoder provider input format must declare PCM16 with a positive sample rate and channel count.", nameof(provider));
        }

        if (!OpusFormatValidation.IsUsableEncodedFormat(provider.OutputFormat))
        {
            throw new ArgumentException("The encoder provider output format must declare Opus with a positive sample rate, channel count, and RTP clock rate when present.", nameof(provider));
        }

        if (!OpusFormatValidation.Matches(provider.InputFormat, provider.OutputFormat))
        {
            throw new ArgumentException("The encoder provider output format must match the PCM input sample rate and channel count.", nameof(provider));
        }

        this.provider = provider;
        InputFormat = provider.InputFormat;
        OutputFormat = provider.OutputFormat;
        encodedScratch = new byte[resolvedOptions.MaxEncodedBytes];
    }

    /// <inheritdoc />
    public AudioFormat InputFormat { get; }

    /// <inheritdoc />
    public EncodedAudioFormat OutputFormat { get; }

    /// <inheritdoc />
    public AudioCodecStatus Encode(in AudioFrame frame, IEncodedAudioFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (disposed)
        {
            return AudioCodecStatus.Disposed;
        }

        AudioCodecStatus validationStatus = ValidateInputFrame(frame.Format, frame.SamplesPerChannel, frame.Data.Length);
        if (validationStatus != AudioCodecStatus.Success)
        {
            return validationStatus;
        }

        AudioCodecStatus status = provider.Encode(
            frame.Data.Span,
            frame.SamplesPerChannel,
            encodedScratch,
            out int bytesWritten);

        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        status = ValidateEncodedByteCount(bytesWritten);
        if (status != AudioCodecStatus.Success || bytesWritten == 0)
        {
            return status;
        }

        var encodedFrame = new EncodedAudioFrame
        {
            Format = OutputFormat,
            Data = encodedScratch.AsMemory(0, bytesWritten),
            Duration = frame.Duration,
            RtpTimestamp = null,
            RtpSequenceNumber = null
        };

        return sink.TryWrite(encodedFrame) ? AudioCodecStatus.Success : AudioCodecStatus.SinkBackpressure;
    }

    /// <inheritdoc />
    public AudioCodecStatus Encode(in AudioFrameView frame, IEncodedAudioFrameViewSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (disposed)
        {
            return AudioCodecStatus.Disposed;
        }

        AudioCodecStatus validationStatus = ValidateInputFrame(frame.Format, frame.SamplesPerChannel, frame.Data.Length);
        if (validationStatus != AudioCodecStatus.Success)
        {
            return validationStatus;
        }

        AudioCodecStatus status = provider.Encode(
            frame.Data,
            frame.SamplesPerChannel,
            encodedScratch,
            out int bytesWritten);

        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        status = ValidateEncodedByteCount(bytesWritten);
        if (status != AudioCodecStatus.Success || bytesWritten == 0)
        {
            return status;
        }

        var encodedFrame = new EncodedAudioFrameView(OutputFormat, encodedScratch.AsSpan(0, bytesWritten), frame.Duration);
        return sink.TryWrite(encodedFrame) ? AudioCodecStatus.Success : AudioCodecStatus.SinkBackpressure;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            provider.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private AudioCodecStatus ValidateInputFrame(AudioFormat format, int samplesPerChannel, int byteLength)
    {
        if (format.SampleFormat != InputFormat.SampleFormat ||
            format.SampleRate != InputFormat.SampleRate ||
            format.ChannelCount != InputFormat.ChannelCount)
        {
            return AudioCodecStatus.UnsupportedFormat;
        }

        long expectedByteLength = (long)samplesPerChannel * format.ChannelCount * 2;
        if (samplesPerChannel <= 0 ||
            format.ChannelCount <= 0 ||
            expectedByteLength > int.MaxValue ||
            byteLength != expectedByteLength)
        {
            return AudioCodecStatus.InvalidInput;
        }

        return AudioCodecStatus.Success;
    }

    private AudioCodecStatus ValidateEncodedByteCount(int bytesWritten)
    {
        if (bytesWritten < 0 || bytesWritten > encodedScratch.Length)
        {
            return AudioCodecStatus.InvalidInput;
        }

        return AudioCodecStatus.Success;
    }
}

internal static class OpusFormatValidation
{
    public static bool IsUsablePcmFormat(AudioFormat format)
    {
        return format.SampleFormat == AudioSampleFormat.Pcm16 &&
            format.SampleRate > 0 &&
            format.ChannelCount > 0;
    }

    public static bool IsUsableEncodedFormat(EncodedAudioFormat format)
    {
        return format.Encoding == AudioEncoding.Opus &&
            format.SampleRate > 0 &&
            format.ChannelCount > 0 &&
            format.RtpClockRate is null or > 0;
    }

    public static bool Matches(AudioFormat pcmFormat, EncodedAudioFormat encodedFormat)
    {
        return pcmFormat.SampleRate == encodedFormat.SampleRate &&
            pcmFormat.ChannelCount == encodedFormat.ChannelCount &&
            (encodedFormat.RtpClockRate ?? encodedFormat.SampleRate) == encodedFormat.SampleRate;
    }
}
