#nullable enable

using System.Diagnostics;
using HPD.Audio.Primitives;
using HPD.Media.Diagnostics;

namespace HPD.Audio.Codecs.G711;

/// <summary>
/// Managed G.711 encoder using an instance-owned scratch buffer.
/// </summary>
public sealed class G711Encoder : IAudioEncoder, IRealtimeAudioEncoder
{
    private readonly byte[] encodedScratch;
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="G711Encoder"/> class.</summary>
    public G711Encoder(AudioFormat inputFormat, EncodedAudioFormat outputFormat, int maxPcmBytes = 8192)
        : this(inputFormat, outputFormat, default, hasTelemetry: false, maxPcmBytes)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="G711Encoder"/> class with cached telemetry emitters.</summary>
    public G711Encoder(
        AudioFormat inputFormat,
        EncodedAudioFormat outputFormat,
        RealtimeMediaTelemetryEmitters telemetry,
        int maxPcmBytes = 8192)
        : this(inputFormat, outputFormat, telemetry, hasTelemetry: true, maxPcmBytes)
    {
    }

    private G711Encoder(
        AudioFormat inputFormat,
        EncodedAudioFormat outputFormat,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry,
        int maxPcmBytes)
    {
        if (inputFormat.SampleFormat != AudioSampleFormat.Pcm16)
        {
            throw new ArgumentException("The input format must be PCM16.", nameof(inputFormat));
        }

        if (!G711Codec.IsSupportedEncoding(outputFormat.Encoding))
        {
            throw new ArgumentException("The output format must be PCMU or PCMA.", nameof(outputFormat));
        }

        if (!G711Codec.IsUsablePcmFormat(inputFormat))
        {
            throw new ArgumentException("The input format must declare PCM16 with a positive sample rate and channel count.", nameof(inputFormat));
        }

        if (!G711Codec.IsUsableEncodedFormat(outputFormat) ||
            outputFormat.SampleRate != inputFormat.SampleRate ||
            outputFormat.ChannelCount != inputFormat.ChannelCount)
        {
            throw new ArgumentException("The output format must match the PCM input sample rate and channel count.", nameof(outputFormat));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPcmBytes);
        if ((maxPcmBytes & 1) != 0)
        {
            throw new ArgumentException("The maximum PCM byte count must be aligned to whole PCM16 samples.", nameof(maxPcmBytes));
        }

        InputFormat = inputFormat;
        OutputFormat = outputFormat;
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        encodedScratch = new byte[checked(maxPcmBytes / 2)];
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

        long started = Stopwatch.GetTimestamp();
        AudioCodecStatus status = G711Codec.Encode(frame.Data.Span, OutputFormat.Encoding, encodedScratch, out int bytesWritten);
        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        if (bytesWritten == 0)
        {
            return AudioCodecStatus.Success;
        }

        var encodedFrame = new EncodedAudioFrame
        {
            Format = OutputFormat,
            Data = encodedScratch.AsMemory(0, bytesWritten),
            Duration = frame.Duration
        };

        if (!sink.TryWrite(encodedFrame))
        {
            return AudioCodecStatus.SinkBackpressure;
        }

        EmitTiming(started, frame.Data.Length, bytesWritten);
        return AudioCodecStatus.Success;
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

        long started = Stopwatch.GetTimestamp();
        AudioCodecStatus status = G711Codec.Encode(frame.Data, OutputFormat.Encoding, encodedScratch, out int bytesWritten);
        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        if (bytesWritten == 0)
        {
            return AudioCodecStatus.Success;
        }

        var encodedFrame = new EncodedAudioFrameView(
            OutputFormat,
            encodedScratch.AsSpan(0, bytesWritten),
            frame.Duration);

        if (!sink.TryWrite(encodedFrame))
        {
            return AudioCodecStatus.SinkBackpressure;
        }

        EmitTiming(started, frame.Data.Length, bytesWritten);
        return AudioCodecStatus.Success;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        disposed = true;
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

    private void EmitTiming(long started, int inputBytes, int outputBytes)
    {
        if (!hasTelemetry)
        {
            return;
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - started;
        _ = telemetry.CodecTiming.Emit(new CodecTimingSample
        {
            Operation = CodecOperation.Encode,
            Encoding = (int)OutputFormat.Encoding,
            ElapsedNanoseconds = elapsedTicks * 1_000_000_000L / Stopwatch.Frequency,
            InputBytes = inputBytes,
            OutputBytes = outputBytes
        });
    }
}
